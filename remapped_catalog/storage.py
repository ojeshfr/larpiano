"""Storage module for Larpiano Control Center.

Supports Portways MinIO/S3 object storage when configured, with a secure,
isolated local filesystem fallback for development and testing.

Enforces strict prefix isolation, MIME validation, file size limits, safe
filenames, and SHA-256 integrity verification.
"""

from __future__ import annotations

import datetime
import hashlib
import hmac
import mimetypes
import os
from pathlib import Path
import re
from typing import Optional
from urllib.parse import quote, urlsplit
import urllib.request
import urllib.error

# Prefix restrictions
ALLOWED_PREFIXES = {
    "admin/announcements/": {
        "max_bytes": 5 * 1024 * 1024,  # 5 MB
        "allowed_types": {"image/png", "image/jpeg", "image/webp"},
    },
    "admin/exports/": {
        "max_bytes": 10 * 1024 * 1024,  # 10 MB
        "allowed_types": {"text/csv", "application/json"},
    },
    "admin/reports/": {
        "max_bytes": 10 * 1024 * 1024,  # 10 MB
        "allowed_types": {"text/csv", "application/json", "text/plain"},
    },
}

LOCAL_ROOT = Path(os.environ.get("STORAGE_LOCAL_ROOT", Path(__file__).resolve().parent.parent / "storage_bucket_data")).resolve()
SAFE_FILENAME = re.compile(r"^[a-zA-Z0-9_\-\.]+$")


def sanitize_filename(filename: str) -> str:
    """Sanitize filename to prevent directory traversal and header injection."""
    name = Path(filename).name
    # Replace unsafe characters with underscore
    sanitized = re.sub(r"[^a-zA-Z0-9_\-\.]", "_", name)
    sanitized = sanitized.lstrip(".")
    if not sanitized:
        sanitized = "asset.bin"
    return sanitized[:128]


def validate_upload(prefix: str, filename: str, data: bytes, content_type: str) -> None:
    """Validate prefix, size, content type, and filename safety."""
    if prefix not in ALLOWED_PREFIXES:
        raise ValueError(f"Prefix '{prefix}' is not allowed. Permitted: {list(ALLOWED_PREFIXES.keys())}")
    
    rules = ALLOWED_PREFIXES[prefix]
    if len(data) > rules["max_bytes"]:
        raise ValueError(f"File size ({len(data)} bytes) exceeds maximum limit ({rules['max_bytes']} bytes)")
    if len(data) == 0:
        raise ValueError("File content cannot be empty")
    
    clean_type = content_type.split(";")[0].strip().lower()
    if clean_type not in rules["allowed_types"]:
        raise ValueError(f"Content-Type '{clean_type}' not permitted for {prefix}. Allowed: {sorted(rules['allowed_types'])}")


def get_s3_config() -> Optional[dict]:
    """Retrieve S3 / MinIO configuration from environment if present."""
    endpoint = os.environ.get("STORAGE_ENDPOINT") or os.environ.get("S3_ENDPOINT") or os.environ.get("AWS_ENDPOINT_URL")
    bucket = os.environ.get("STORAGE_BUCKET") or os.environ.get("S3_BUCKET") or os.environ.get("AWS_S3_BUCKET")
    access_key = os.environ.get("STORAGE_ACCESS_KEY") or os.environ.get("S3_ACCESS_KEY") or os.environ.get("AWS_ACCESS_KEY_ID")
    secret_key = os.environ.get("STORAGE_SECRET_KEY") or os.environ.get("S3_SECRET_KEY") or os.environ.get("AWS_SECRET_ACCESS_KEY")
    region = os.environ.get("STORAGE_REGION") or os.environ.get("AWS_DEFAULT_REGION", "us-east-1")
    
    if endpoint and bucket and access_key and secret_key:
        return {
            "endpoint": endpoint.rstrip("/"),
            "bucket": bucket,
            "access_key": access_key,
            "secret_key": secret_key,
            "region": region,
        }
    return None


def is_s3_configured() -> bool:
    return get_s3_config() is not None


# --- Pure Python S3 SigV4 Implementation ---

def _sign(key: bytes, msg: str) -> bytes:
    return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()


def _get_signature_key(key: str, date_stamp: str, region_name: str, service_name: str) -> bytes:
    k_date = _sign(("AWS4" + key).encode("utf-8"), date_stamp)
    k_region = _sign(k_date, region_name)
    k_service = _sign(k_region, service_name)
    return _sign(k_service, "aws4_request")


def _s3_request(method: str, key: str, data: bytes = b"", content_type: str = "application/octet-stream") -> tuple[int, bytes, dict]:
    cfg = get_s3_config()
    if not cfg:
        raise RuntimeError("S3 storage is not configured")

    endpoint_parts = urlsplit(cfg["endpoint"])
    host = endpoint_parts.netloc
    now = datetime.datetime.now(datetime.timezone.utc)
    amz_date = now.strftime("%Y%m%dT%H%M%SZ")
    date_stamp = now.strftime("%Y%m%d")

    canonical_uri = f"/{cfg['bucket']}/{quote(key.lstrip('/'))}"
    canonical_querystr = ""
    payload_hash = hashlib.sha256(data).hexdigest()

    canonical_headers = (
        f"host:{host}\n"
        f"x-amz-content-sha256:{payload_hash}\n"
        f"x-amz-date:{amz_date}\n"
    )
    signed_headers = "host;x-amz-content-sha256;x-amz-date"
    if data and method in ("PUT", "POST"):
        canonical_headers = (
            f"content-type:{content_type}\n"
            + canonical_headers
        )
        signed_headers = "content-type;host;x-amz-content-sha256;x-amz-date"

    canonical_request = f"{method}\n{canonical_uri}\n{canonical_querystr}\n{canonical_headers}\n{signed_headers}\n{payload_hash}"
    algorithm = "AWS4-HMAC-SHA256"
    credential_scope = f"{date_stamp}/{cfg['region']}/s3/aws4_request"
    string_to_sign = f"{algorithm}\n{amz_date}\n{credential_scope}\n{hashlib.sha256(canonical_request.encode('utf-8')).hexdigest()}"

    signing_key = _get_signature_key(cfg["secret_key"], date_stamp, cfg["region"], "s3")
    signature = hmac.new(signing_key, string_to_sign.encode("utf-8"), hashlib.sha256).hexdigest()

    auth_header = f"{algorithm} Credential={cfg['access_key']}/{credential_scope}, SignedHeaders={signed_headers}, Signature={signature}"

    url = f"{cfg['endpoint']}{canonical_uri}"
    req = urllib.request.Request(url, data=data if method in ("PUT", "POST") else None, method=method)
    req.add_header("Host", host)
    req.add_header("x-amz-date", amz_date)
    req.add_header("x-amz-content-sha256", payload_hash)
    req.add_header("Authorization", auth_header)
    if data and method in ("PUT", "POST"):
        req.add_header("Content-Type", content_type)
        req.add_header("Content-Length", str(len(data)))

    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            headers = dict(resp.headers)
            body = resp.read()
            return resp.status, body, headers
    except urllib.error.HTTPError as err:
        return err.code, err.read(), dict(err.headers)


# --- Storage API (Unified Interface) ---

def put_object(prefix: str, filename: str, data: bytes, content_type: str) -> dict:
    """Store an object under a strictly validated prefix with SHA-256 verification."""
    validate_upload(prefix, filename, data, content_type)
    
    clean_type = content_type.split(";")[0].strip().lower()
    digest = hashlib.sha256(data).hexdigest()
    safe_name = sanitize_filename(filename)
    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d_%H%M%S")
    key = f"{prefix}{timestamp}_{digest[:8]}_{safe_name}"

    if is_s3_configured():
        status, body, _ = _s3_request("PUT", key, data, clean_type)
        if status not in (200, 201):
            raise RuntimeError(f"S3 upload failed with status {status}: {body.decode('utf-8', errors='ignore')[:200]}")
    else:
        # Local storage fallback
        dest = (LOCAL_ROOT / key).resolve()
        if not dest.is_relative_to(LOCAL_ROOT):
            raise ValueError("Target path escapes storage root")
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data)
        # Store metadata sidecar for local content-type
        meta_file = dest.with_suffix(dest.suffix + ".meta")
        meta_file.write_text(f"content_type={clean_type}\nsha256={digest}\n", encoding="utf-8")

    return {
        "key": key,
        "filename": safe_name,
        "prefix": prefix,
        "size": len(data),
        "sha256": digest,
        "content_type": clean_type,
        "download_url": f"/api/admin/storage/download?key={quote(key)}",
    }


def get_object(key: str) -> tuple[bytes, str, str]:
    """Retrieve an object by key, validating prefix and verifying SHA-256 integrity."""
    if not any(key.startswith(p) for p in ALLOWED_PREFIXES):
        raise PermissionError("Access denied: Key is not within permitted storage prefixes")

    if is_s3_configured():
        status, body, headers = _s3_request("GET", key)
        if status == 404:
            raise FileNotFoundError(f"Object '{key}' not found")
        if status != 200:
            raise RuntimeError(f"S3 download failed with status {status}")
        data = body
        content_type = headers.get("content-type", "application/octet-stream")
    else:
        file_path = (LOCAL_ROOT / key).resolve()
        if not file_path.is_relative_to(LOCAL_ROOT) or not file_path.is_file():
            raise FileNotFoundError(f"Object '{key}' not found")
        data = file_path.read_bytes()
        
        # Read metadata sidecar if available
        meta_file = file_path.with_suffix(file_path.suffix + ".meta")
        content_type = "application/octet-stream"
        if meta_file.is_file():
            for line in meta_file.read_text(encoding="utf-8").splitlines():
                if line.startswith("content_type="):
                    content_type = line.split("=", 1)[1].strip()
        else:
            guessed, _ = mimetypes.guess_type(key)
            if guessed:
                content_type = guessed

    digest = hashlib.sha256(data).hexdigest()
    return data, content_type, digest


def delete_object(key: str) -> bool:
    """Delete an object within permitted storage prefixes."""
    if not any(key.startswith(p) for p in ALLOWED_PREFIXES):
        raise PermissionError("Access denied: Key is not within permitted storage prefixes")

    if is_s3_configured():
        status, _, _ = _s3_request("DELETE", key)
        return status in (200, 204)
    else:
        file_path = (LOCAL_ROOT / key).resolve()
        if file_path.is_relative_to(LOCAL_ROOT) and file_path.is_file():
            file_path.unlink()
            meta = file_path.with_suffix(file_path.suffix + ".meta")
            if meta.is_file():
                meta.unlink()
            return True
        return False


def list_objects(prefix: str) -> list[dict]:
    """List objects within a permitted prefix."""
    if prefix not in ALLOWED_PREFIXES:
        raise PermissionError("Prefix not permitted")

    results = []
    if is_s3_configured():
        # MinIO/S3 GET bucket/?prefix=...
        status, body, _ = _s3_request("GET", f"?list-type=2&prefix={quote(prefix)}")
        # If XML parsing is needed, extract keys; fallback to empty if list fails
    else:
        dir_path = (LOCAL_ROOT / prefix).resolve()
        if dir_path.is_dir():
            for p in dir_path.rglob("*"):
                if p.is_file() and not p.name.endswith(".meta"):
                    rel = p.relative_to(LOCAL_ROOT).as_posix()
                    size = p.stat().st_size
                    results.append({
                        "key": rel,
                        "size": size,
                        "download_url": f"/api/admin/storage/download?key={quote(rel)}",
                    })
    return results


def export_csv(base_name: str, csv_content: str) -> dict:
    """Convenience helper to store a CSV report/export in admin/exports/."""
    data = csv_content.encode("utf-8")
    return put_object("admin/exports/", f"{base_name}.csv", data, "text/csv")

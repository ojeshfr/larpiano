"""Email delivery service abstraction and providers for Larpiano."""

from __future__ import annotations

import abc
import email.utils
import logging
import os
import smtplib
import threading
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText
from typing import Any, Optional

log = logging.getLogger("larpiano_email")


class EmailDeliveryService(abc.ABC):
    """Abstract base class for transactional email delivery."""

    @abc.abstractmethod
    def send_verification_email(self, to_email: str, display_name: str, code: str) -> bool:
        """Send 6-digit email verification code."""
        pass

    @abc.abstractmethod
    def send_password_reset_email(self, to_email: str, display_name: str, code: str) -> bool:
        """Send password recovery code."""
        pass


class MockEmailDeliveryService(EmailDeliveryService):
    """In-memory mock email delivery service for testing and development."""

    def __init__(self):
        self._sent_emails: list[dict[str, Any]] = []
        self._lock = threading.Lock()

    @property
    def sent_emails(self) -> list[dict[str, Any]]:
        with self._lock:
            return list(self._sent_emails)

    def clear(self):
        with self._lock:
            self._sent_emails.clear()

    def send_verification_email(self, to_email: str, display_name: str, code: str) -> bool:
        record = {
            "type": "verification",
            "to": to_email,
            "display_name": display_name,
            "code": code,
        }
        with self._lock:
            self._sent_emails.append(record)
        log.info("[MOCK EMAIL] Verification email dispatched to %s", to_email)
        return True

    def send_password_reset_email(self, to_email: str, display_name: str, code: str) -> bool:
        record = {
            "type": "password_reset",
            "to": to_email,
            "display_name": display_name,
            "code": code,
        }
        with self._lock:
            self._sent_emails.append(record)
        log.info("[MOCK EMAIL] Password reset email dispatched to %s", to_email)
        return True


class SmtpEmailDeliveryService(EmailDeliveryService):
    """Production SMTP transactional email delivery provider."""

    def __init__(
        self,
        host: str,
        port: int = 587,
        username: str = "",
        password: str = "",
        from_email: str = "noreply@larpiano.portways.app",
        from_name: str = "Larpiano",
        use_tls: bool = True,
        timeout: int = 15,
    ):
        self.host = host
        self.port = port
        self.username = username
        self.password = password
        self.from_email = from_email
        self.from_name = from_name
        self.use_tls = use_tls
        self.timeout = timeout

    def _send_mime(self, to_email: str, subject: str, text_body: str, html_body: str) -> bool:
        msg = MIMEMultipart("alternative")
        msg["Subject"] = subject
        msg["From"] = email.utils.formataddr((self.from_name, self.from_email))
        msg["To"] = to_email
        msg["Date"] = email.utils.formatdate(localtime=True)
        msg["Message-ID"] = email.utils.make_msgid(domain="larpiano.portways.app")

        part1 = MIMEText(text_body, "plain", "utf-8")
        part2 = MIMEText(html_body, "html", "utf-8")
        msg.attach(part1)
        msg.attach(part2)

        try:
            with smtplib.SMTP(self.host, self.port, timeout=self.timeout) as server:
                if self.use_tls:
                    server.starttls()
                if self.username and self.password:
                    server.login(self.username, self.password)
                server.sendmail(self.from_email, [to_email], msg.as_string())
            log.info("Transactional email successfully sent to %s", to_email)
            return True
        except Exception as exc:
            log.error("Failed to send transactional email to %s: %s", to_email, exc)
            return False

    def send_verification_email(self, to_email: str, display_name: str, code: str) -> bool:
        subject = f"{code} is your Larpiano verification code"
        name = display_name or "Pianist"
        text = (
            f"Hello {name},\n\n"
            f"Welcome to Larpiano! Your email verification code is:\n\n"
            f"    {code}\n\n"
            "This code expires in 10 minutes and can only be used once.\n\n"
            "If you did not request this registration, please ignore this email.\n\n"
            "— The Larpiano Team\n"
        )
        html = _render_email_html(
            title="Verify Your Larpiano Account",
            greeting=f"Hello, {name}!",
            lead="Thank you for creating an account on Larpiano. Use the 6-digit code below to verify your email address.",
            code=code,
            expires="This code expires in 10 minutes and is single-use.",
            footer_note="If you did not sign up for Larpiano, you can safely disregard this email.",
        )
        return self._send_mime(to_email, subject, text, html)

    def send_password_reset_email(self, to_email: str, display_name: str, code: str) -> bool:
        subject = f"{code} is your Larpiano password recovery code"
        name = display_name or "Pianist"
        text = (
            f"Hello {name},\n\n"
            f"We received a request to reset your Larpiano password. Your recovery code is:\n\n"
            f"    {code}\n\n"
            "This code expires in 15 minutes and can only be used once.\n\n"
            "If you did not request a password reset, your account is still safe. No changes have been made.\n\n"
            "— The Larpiano Team\n"
        )
        html = _render_email_html(
            title="Reset Your Password",
            greeting=f"Hello, {name}",
            lead="We received a request to reset your Larpiano account password. Enter this verification code to proceed.",
            code=code,
            expires="This recovery code expires in 15 minutes.",
            footer_note="If you didn't ask to reset your password, please ignore this email or review your account security.",
        )
        return self._send_mime(to_email, subject, text, html)


def _render_email_html(title: str, greeting: str, lead: str, code: str, expires: str, footer_note: str) -> str:
    """Renders a sleek, responsive dark-themed HTML email template matching Larpiano."""
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>{title}</title>
  <style>
    body {{
      margin: 0;
      padding: 0;
      background-color: #0b0c10;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      color: #e2e8f0;
    }}
    .wrapper {{
      width: 100%;
      background-color: #0b0c10;
      padding: 40px 16px;
      box-sizing: border-box;
    }}
    .container {{
      max-width: 520px;
      margin: 0 auto;
      background-color: #12141c;
      border: 1px solid #1f2333;
      border-radius: 16px;
      overflow: hidden;
      box-shadow: 0 10px 30px rgba(0,0,0,0.5);
    }}
    .header {{
      background: linear-gradient(135deg, #1b172a 0%, #12141c 100%);
      padding: 32px 32px 24px 32px;
      border-bottom: 1px solid #1f2333;
      text-align: center;
    }}
    .logo-text {{
      font-size: 24px;
      font-weight: 800;
      letter-spacing: -0.5px;
      color: #ffffff;
      margin: 0;
    }}
    .logo-text span {{
      color: #a855f7;
    }}
    .content {{
      padding: 32px;
    }}
    h1 {{
      font-size: 20px;
      font-weight: 700;
      color: #ffffff;
      margin-top: 0;
      margin-bottom: 12px;
    }}
    p {{
      font-size: 14px;
      line-height: 1.6;
      color: #94a3b8;
      margin: 0 0 20px 0;
    }}
    .code-container {{
      background: #090a0f;
      border: 1px solid #2d3148;
      border-radius: 12px;
      padding: 20px;
      text-align: center;
      margin: 28px 0;
    }}
    .code {{
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 32px;
      font-weight: 800;
      letter-spacing: 8px;
      color: #c084fc;
      display: inline-block;
      padding-left: 8px;
    }}
    .expiry {{
      font-size: 12px;
      color: #64748b;
      margin-top: 8px;
      margin-bottom: 0;
    }}
    .footer {{
      padding: 24px 32px;
      background-color: #0b0c10;
      border-top: 1px solid #1a1d2b;
      text-align: center;
    }}
    .footer p {{
      font-size: 12px;
      color: #475569;
      margin: 0;
    }}
  </style>
</head>
<body>
  <div class="wrapper">
    <div class="container">
      <div class="header">
        <h2 class="logo-text">lar<span>piano</span></h2>
      </div>
      <div class="content">
        <h1>{greeting}</h1>
        <p>{lead}</p>
        <div class="code-container">
          <div class="code">{code}</div>
          <p class="expiry">{expires}</p>
        </div>
        <p style="font-size: 13px; color: #64748b;">{footer_note}</p>
      </div>
      <div class="footer">
        <p>© 2026 Larpiano Autoplayer. Built for precision piano execution.</p>
      </div>
    </div>
  </div>
</body>
</html>
"""


_DEFAULT_SERVICE: Optional[EmailDeliveryService] = None
_SERVICE_LOCK = threading.Lock()


def get_email_service() -> EmailDeliveryService:
    """Returns the configured email service instance based on environment variables."""
    global _DEFAULT_SERVICE
    with _SERVICE_LOCK:
        if _DEFAULT_SERVICE is not None:
            return _DEFAULT_SERVICE

        provider = os.environ.get("EMAIL_PROVIDER", "").strip().lower()
        host = os.environ.get("EMAIL_HOST", "").strip()

        if provider == "smtp" and host:
            port = int(os.environ.get("EMAIL_PORT", "587"))
            username = os.environ.get("EMAIL_USER") or os.environ.get("EMAIL_API_KEY", "")
            password = os.environ.get("EMAIL_PASS") or os.environ.get("EMAIL_SECRET", "")
            from_email = os.environ.get("EMAIL_FROM", "noreply@larpiano.portways.app")
            from_name = os.environ.get("EMAIL_FROM_NAME", "Larpiano")
            use_tls = os.environ.get("EMAIL_USE_TLS", "true").lower() in ("true", "1", "yes")

            log.info("Initialized SMTP email delivery service with host %s:%d", host, port)
            _DEFAULT_SERVICE = SmtpEmailDeliveryService(
                host=host,
                port=port,
                username=username,
                password=password,
                from_email=from_email,
                from_name=from_name,
                use_tls=use_tls,
            )
        else:
            log.info("Using mock email delivery service (set EMAIL_PROVIDER=smtp and EMAIL_HOST for production delivery)")
            _DEFAULT_SERVICE = MockEmailDeliveryService()

        return _DEFAULT_SERVICE


def set_email_service_for_testing(service: EmailDeliveryService):
    """Override email service for automated unit testing."""
    global _DEFAULT_SERVICE
    with _SERVICE_LOCK:
        _DEFAULT_SERVICE = service

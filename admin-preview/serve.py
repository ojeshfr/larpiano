import http.server
import socketserver
import os
import urllib.request
import urllib.error

BACKEND_API_URL = os.environ.get("BACKEND_API_URL", "http://127.0.0.1:8088")

class NoCacheHTTPRequestHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0')
        self.send_header('Pragma', 'no-cache')
        self.send_header('Expires', '0')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, POST, PATCH, DELETE, OPTIONS, HEAD')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Requested-With')
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(204)
        self.end_headers()

    def proxy_api(self, method):
        if not self.path.startswith("/api/"):
            return False
        
        target_url = f"{BACKEND_API_URL}{self.path}"
        headers = {}
        for k, v in self.headers.items():
            if k.lower() not in ("host", "content-length"):
                headers[k] = v

        body = None
        content_length = int(self.headers.get("Content-Length", 0))
        if content_length > 0:
            body = self.rfile.read(content_length)

        req = urllib.request.Request(target_url, data=body, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=10) as resp:
                resp_body = resp.read()
                self.send_response(resp.status)
                for k, v in resp.headers.items():
                    if k.lower() not in ("content-length", "transfer-encoding", "access-control-allow-origin"):
                        self.send_header(k, v)
                self.send_header("Content-Length", str(len(resp_body)))
                self.end_headers()
                self.wfile.write(resp_body)
                return True
        except urllib.error.HTTPError as err:
            err_body = err.read()
            self.send_response(err.code)
            for k, v in err.headers.items():
                if k.lower() not in ("content-length", "transfer-encoding", "access-control-allow-origin"):
                    self.send_header(k, v)
            self.send_header("Content-Length", str(len(err_body)))
            self.end_headers()
            self.wfile.write(err_body)
            return True
        except Exception as exc:
            self.send_response(502)
            self.send_header("Content-Type", "application/json")
            err_msg = b'{"error":"Backend API proxy unavailable"}'
            self.send_header("Content-Length", str(len(err_msg)))
            self.end_headers()
            self.wfile.write(err_msg)
            return True

    def do_GET(self):
        if self.proxy_api("GET"):
            return
        super().do_GET()

    def do_POST(self):
        if self.proxy_api("POST"):
            return
        self.send_error(404)

    def do_PATCH(self):
        if self.proxy_api("PATCH"):
            return
        self.send_error(404)

    def do_DELETE(self):
        if self.proxy_api("DELETE"):
            return
        self.send_error(404)

class ThreadingHTTPServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    request_queue_size = 128
    daemon_threads = True

if __name__ == '__main__':
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    with ThreadingHTTPServer(('127.0.0.1', 8089), NoCacheHTTPRequestHandler) as httpd:
        print("Serving admin-preview on http://127.0.0.1:8089 (Multithreaded, no-cache, API proxy)...")
        httpd.serve_forever()

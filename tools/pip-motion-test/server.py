# Mock engine for the Pip motion test: serves the real popup page and a fixed session state (long-poll).
import http.server, json, sys, time, re
import os
src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '../../src/QuickPrompt.Core/ChatPage.cs'), encoding='utf-8').read().split('\n')
start = next(i for i, l in enumerate(src) if 'Html = """' in l) + 1
end = next(i for i in range(start, len(src)) if src[i].startswith('""";'))
html = '\n'.join(src[start:end]).encode()
state = {"v": 2, "busy": False, "status": "", "title": "", "provider": "claude", "providerName": "Claude", "model": "", "appWindow": True,
  "draft": {"instruction": "", "context": "", "images": []}, "notice": "", "pasteMode": False, "connection": "cli",
  "showSettings": False, "showHistory": False, "showActions": False, "fields": [], "presetPrompt": "",
  "popupScale": 7, "iconScale": 25, "glassTint": 0, "keepPip": True, "startCollapsed": True, "messages": []}
polls = {"n": 0}
class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def send(self, body, ctype="application/json", code=200):
        self.send_response(code); self.send_header("Content-Type", ctype); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    def do_GET(self):
        p = self.path.split('?')[0]
        if p == '/s/tok/': return self.send(html, "text/html; charset=utf-8")
        if p.endswith('/state'):
            v = int(re.search(r'v=(\d+)', self.path).group(1)) if re.search(r'v=(\d+)', self.path) else 0
            if v >= state["v"]: time.sleep(20)   # long-poll: nothing new
            return self.send(json.dumps(state).encode())
        if p.endswith('/presets'): return self.send(b'[{"id":"b1","title":"Explain","provider":"claude","providerName":"Claude","connection":"cli"}]')
        return self.send(b'[]')
    def do_POST(self):
        self.rfile.read(int(self.headers.get('Content-Length') or 0)); self.send(b'{"ok":true}')
http.server.ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1]) if len(sys.argv) > 1 else 9977), H).serve_forever()

#!/usr/bin/env python3
import http.server
import os

PORT = 8787


class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {
        **http.server.SimpleHTTPRequestHandler.extensions_map,
        '.js': 'text/javascript',
        '.svg': 'image/svg+xml',
        '.html': 'text/html; charset=utf-8',
    }

    def do_POST(self):
        if self.path == '/result':
            length = int(self.headers.get('Content-Length', 0))
            body = self.rfile.read(length)
            with open('results.jsonl', 'ab') as f:
                f.write(body + b'\n')
            self.send_response(204)
            self.end_headers()
        else:
            self.send_response(404)
            self.end_headers()

    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()


os.chdir(os.path.dirname(os.path.abspath(__file__)))
print(f'serving on :{PORT}', flush=True)
http.server.ThreadingHTTPServer(('', PORT), Handler).serve_forever()

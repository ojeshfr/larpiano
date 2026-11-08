import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
const source = await readFile(new URL("../admin-preview/js/components/safeText.js", import.meta.url), "utf8");
const { escapeText, safeHttpsUrl } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
for (const payload of [
  '<script>alert(1)</script>',
  '<img src=x onerror=alert(1)>',
  '<svg onload=alert(1)>',
  '" autofocus onfocus="alert(1)',
  { injected: '<script>alert(1)</script>' },
  ['<svg onload=alert(1)>']
]) {
  const encoded = escapeText(payload);
  assert(!/[<>"']/.test(encoded), `Unsafe output: ${encoded}`);
}
for (const url of ['javascript:alert(1)', 'data:text/html,<script>alert(1)</script>', 'https://user:pass@example.com/', {}, []]) {
  assert.equal(safeHttpsUrl(url), '');
}
assert.equal(safeHttpsUrl('https://example.com/a'), 'https://example.com/a');
console.log('Text encoding and URL checks passed');

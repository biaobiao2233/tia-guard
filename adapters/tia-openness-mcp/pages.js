"use strict";
const { AdapterError, payload, object } = require("./contract.js");

// Public v2.7.11 GetExport character pages, never arbitrary tool forwarding.
async function completeResult(first, nextPage, maxChars = 2_000_000) {
  const initial = payload(first);
  if (initial.meta?.truncated !== true) return requireComplete(initial);
  const id = initial.meta.exportId, total = initial.meta.totalLength;
  if (typeof id !== "string" || !/^ex_[A-Za-z0-9_]+$/.test(id) ||
      !Number.isSafeInteger(total) || total < 1 || total > maxChars) fail();
  let current = initial, text = "", pages = 0;
  while (true) {
    const m = current.meta;
    if (!object(m) || m.exportId !== id || m.totalLength !== total || m.offset !== text.length ||
        typeof current.message !== "string" || m.returned !== current.message.length ||
        m.returned < 1 || text.length + m.returned > total || ++pages > 200) fail();
    text += current.message;
    if (m.eof === true) {
      if (text.length !== total || m.nextOffset != null) fail();
      break;
    }
    if (m.eof !== false || m.nextOffset !== text.length || text.length >= total) fail();
    current = payload(await nextPage(id, text.length));
    if (current.meta?.ok !== true) fail();
  }
  let parsed;
  try { parsed = JSON.parse(text); } catch { fail(); }
  return requireComplete(payload({ structuredContent: parsed }));
}
function requireComplete(value) {
  if (value.meta?.complete === false || value.meta?.truncated === true || value.meta?.ok === false ||
      value.data?.complete === false || value.data?.truncated === true) {
    throw new AdapterError("incomplete-upstream-read", "contract");
  }
  return value;
}
function fail() { throw new AdapterError("invalid-upstream-pagination", "contract"); }
module.exports = { completeResult, requireComplete };

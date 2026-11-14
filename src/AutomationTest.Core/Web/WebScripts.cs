namespace AutomationTest.Core.Web
{
    /// <summary>
    /// The JavaScript the Web actions run inside the page. The selector, frame and index are passed as arguments (JSON text).
    /// </summary>
    internal static class WebScripts
    {
        // resolve(sel, frame, index): the elements for a CSS selector, or for "text=Label" the innermost visible-text matches, inside
        // the (same-origin) iframes named by frame ("sel >>> sel"); index is the match to use (1 = first; beyond the last = the last).
        // Returns { el, ox, oy, count } (ox, oy: where the frame's document sits in the page), { error }, or null when there is no
        // match (or the frame is not there yet).
        public const string ResolveJs = @"function resolve(sel, frame, index) {
  var doc = document, ox = 0, oy = 0;
  var frames = frame ? frame.split('>>>') : [];
  for (var i = 0; i < frames.length; i++) {
    var f = doc.querySelector(frames[i].trim());
    if (!f) return null;
    var inner = null;
    try { inner = f.contentDocument; } catch (err) { inner = null; }
    if (!inner) return { error: 'The frame ' + frames[i].trim() + ' is from another site.', cross: true };
    var fr = f.getBoundingClientRect();
    ox += fr.left + f.clientLeft; oy += fr.top + f.clientTop;
    doc = inner;
  }
  var list;
  if (sel.indexOf('text=') === 0) {
    var want = sel.slice(5).trim().toLowerCase();
    var hits = Array.prototype.slice.call(doc.querySelectorAll('body *')).filter(function (e) {
      var t = (e.innerText || e.value || '').trim().toLowerCase();
      return t.length > 0 && t.indexOf(want) >= 0;
    });
    list = hits.filter(function (e) { return !hits.some(function (o) { return o !== e && e.contains(o); }); });
  } else {
    list = Array.prototype.slice.call(doc.querySelectorAll(sel));
  }
  if (list.length === 0) return null;
  var n = Math.min(Math.max(index || 1, 1), list.length);
  return { el: list[n - 1], ox: ox, oy: oy, count: list.length };
}";

        // Reports where the element is (in page pixels).
        public const string Locate = @"(function (sel, frame, index) {" + ResolveJs + @"
  var hit;
  try { hit = resolve(sel, frame, index); } catch (err) { return { error: 'Bad selector: ' + err.message }; }
  if (hit && hit.error) return { error: hit.error, cross: hit.cross };
  if (!hit) return { found: false };
  var el = hit.el;
  el.scrollIntoView({ block: 'center', inline: 'center' });
  // The frames moved while scrolling: measure again.
  hit = resolve(sel, frame, index);
  var r = el.getBoundingClientRect();
  var visible = r.width > 0 && r.height > 0 && getComputedStyle(el).visibility !== 'hidden';
  return { found: true, visible: visible, x: hit.ox + r.left + r.width / 2, y: hit.oy + r.top + r.height / 2 };
})";

        // Walks the iframes of the chain in this document while they are of the same site. Returns { missing: i } when the frame i is not
        // there, { cross: i, ox, oy } when it is of another site (ox, oy: where its document starts in this one), { cross: -1 } when all
        // can be entered.
        public const string ProbeFrames = @"(function (frame) {
  var segs = frame.split('>>>'), doc = document, ox = 0, oy = 0;
  for (var i = 0; i < segs.length; i++) {
    var f = doc.querySelector(segs[i].trim());
    if (!f) return { missing: i };
    var inner = null;
    try { inner = f.contentDocument; } catch (err) { inner = null; }
    var r = f.getBoundingClientRect();
    var nx = ox + r.left + f.clientLeft, ny = oy + r.top + f.clientTop;
    if (!inner) return { cross: i, ox: nx, oy: ny };
    ox = nx; oy = ny; doc = inner;
  }
  return { cross: -1 };
})";

        // The iframe element number upTo of the chain (as an object, to ask the browser for its frame).
        public const string FrameElement = @"(function (frame, upTo) {
  var segs = frame.split('>>>'), doc = document, el = null;
  for (var i = 0; i <= upTo; i++) {
    el = doc.querySelector(segs[i].trim());
    if (i < upTo) doc = el.contentDocument;
  }
  return el;
})";

        // Focuses the element and selects its content, so typed text replaces it.
        public const string Focus = @"(function (sel, frame, index) {" + ResolveJs + @"
  var hit = resolve(sel, frame, index);
  if (!hit || hit.error) return false;
  var e = hit.el;
  e.focus();
  if (e.select) e.select(); else e.ownerDocument.execCommand('selectAll');
  return true;
})";

        // How many elements match (0 when none, or when the frame is not there).
        public const string Count = @"(function (sel, frame) {" + ResolveJs + @"
  var hit;
  try { hit = resolve(sel, frame, 1); } catch (err) { return { error: 'Bad selector: ' + err.message }; }
  if (!hit) return 0;
  if (hit.error) return { error: hit.error };
  return hit.count;
})";

        // Reads something of the element: what is Text, Value, SelectedText, Checked, Html, OuterHtml or Attribute.
        // Returns null when the element is not there (anymore).
        public const string Read = @"(function (sel, frame, index, what, attr) {" + ResolveJs + @"
  var hit = resolve(sel, frame, index);
  if (!hit || hit.error) return null;
  var e = hit.el, tag = e.tagName;
  function visibleText() { return String(e.innerText != null ? e.innerText : e.textContent).trim(); }
  switch (what) {
    case 'Value':
      return e.value !== undefined && e.value !== null ? String(e.value) : String(e.getAttribute('value') || '');
    case 'SelectedText':
      if (tag === 'SELECT') return Array.prototype.map.call(e.selectedOptions, function (o) { return o.text.trim(); }).join(', ');
      if (tag === 'INPUT' && (e.type === 'checkbox' || e.type === 'radio')) {
        if (!e.checked) return '';
        var l = e.labels && e.labels[0];
        return l ? l.innerText.trim() : String(e.value);
      }
      return visibleText();
    case 'Checked':
      if (typeof e.checked === 'boolean') return e.checked;
      if (tag === 'OPTION') return !!e.selected;
      return e.getAttribute('aria-checked') === 'true';
    case 'Html': return e.innerHTML;
    case 'OuterHtml': return e.outerHTML;
    case 'Attribute':
      var v = e.getAttribute(attr);
      if (v === null && attr in e && typeof e[attr] !== 'object' && typeof e[attr] !== 'function') v = e[attr];
      return v === null || v === undefined ? '' : String(v);
    default:
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return String(e.value);
      return visibleText();
  }
})";
    }
}

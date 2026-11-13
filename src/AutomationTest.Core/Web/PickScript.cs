namespace AutomationTest.Core.Web
{
    /// <summary>
    /// The script that lets the user point at an element on the page. It draws a frame around the element under the mouse,
    /// swallows the click, and leaves { selector, frame } (or { cancelled: true }) as JSON text in window.__atPick of the top page.
    /// </summary>
    internal static class PickScript
    {
        public const string Install = @"(function () {
  var top = window;
  if (top.__atPickCleanup) top.__atPickCleanup();
  top.__atPick = null;

  var box = document.createElement('div');
  box.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;border:2px solid #e11;background:rgba(255,0,0,.12);display:none;left:0;top:0;box-sizing:border-box';
  var tip = document.createElement('div');
  tip.style.cssText = 'position:fixed;z-index:2147483647;pointer-events:none;left:8px;top:8px;background:#222;color:#fff;font:13px sans-serif;padding:4px 8px;border-radius:4px';
  tip.textContent = 'Click the element to pick it. Esc cancels.';
  document.documentElement.appendChild(box);
  document.documentElement.appendChild(tip);

  function esc(s) { return (window.CSS && CSS.escape) ? CSS.escape(s) : s.replace(/[^\w-]/g, '\\$&'); }
  function unique(doc, sel) { try { return doc.querySelectorAll(sel).length === 1; } catch (e) { return false; } }
  // Ids made by frameworks (long digit runs, hashes) change between visits: do not rely on them.
  function stableId(id) { return !!id && !/\d{4,}/.test(id) && !/[0-9a-f]{8,}/i.test(id) && !/^(:r|ember|react|radix|mui-)/i.test(id); }
  function textCount(doc, t) {
    var want = t.toLowerCase();
    var hits = Array.prototype.slice.call(doc.querySelectorAll('body *')).filter(function (e) {
      var x = (e.innerText || e.value || '').trim().toLowerCase();
      return x.length > 0 && x.indexOf(want) >= 0;
    });
    return hits.filter(function (e) { return !hits.some(function (o) { return o !== e && e.contains(o); }); }).length;
  }

  function selectorFor(el) {
    var doc = el.ownerDocument, tag = el.tagName.toLowerCase(), s;
    if (stableId(el.id) && unique(doc, '#' + esc(el.id))) return '#' + esc(el.id);
    var attrs = ['data-testid', 'data-test', 'data-qa', 'data-cy', 'name', 'aria-label', 'placeholder', 'title', 'alt'];
    for (var i = 0; i < attrs.length; i++) {
      var v = el.getAttribute(attrs[i]);
      if (v && v.length < 80) {
        s = tag + '[' + attrs[i] + '=""' + v.replace(/""/g, '\\""') + '""]';
        if (unique(doc, s)) return s;
      }
    }
    var label = (el.innerText || el.value || '').trim();
    var clickable = /^(button|a|label|summary)$/.test(tag) || el.getAttribute('role') === 'button';
    if (clickable && label && label.length <= 40 && label.indexOf('\n') < 0 && textCount(doc, label) === 1) return 'text=' + label;
    var parts = [], cur = el;
    while (cur && cur.nodeType === 1 && cur !== doc.documentElement) {
      var part = cur.tagName.toLowerCase();
      if (cur !== el && stableId(cur.id)) { parts.unshift('#' + esc(cur.id)); s = parts.join(' > '); if (unique(doc, s)) return s; break; }
      var cls = Array.prototype.slice.call(cur.classList).filter(function (c) { return !/\d{3,}/.test(c) && c.length < 30; }).slice(0, 2);
      if (cls.length) part += '.' + cls.map(esc).join('.');
      var p = cur.parentElement;
      if (p) {
        var same = Array.prototype.filter.call(p.children, function (c) { return c.tagName === cur.tagName; });
        if (same.length > 1) part += ':nth-of-type(' + (same.indexOf(cur) + 1) + ')';
      }
      parts.unshift(part);
      s = parts.join(' > ');
      if (unique(doc, s)) return s;
      cur = cur.parentElement;
    }
    return parts.join(' > ');
  }

  function framePath(el) {
    var list = [], w = el.ownerDocument.defaultView;
    while (w !== top && w.frameElement) { list.unshift(selectorFor(w.frameElement)); w = w.parent; }
    return list.join(' >>> ');
  }

  function offsetOf(el) {
    var ox = 0, oy = 0, w = el.ownerDocument.defaultView;
    while (w !== top && w.frameElement) { var fr = w.frameElement.getBoundingClientRect(); ox += fr.left; oy += fr.top; w = w.parent; }
    return [ox, oy];
  }

  var hooked = [];
  function onMove(e) {
    var el = e.target;
    if (!el || el.nodeType !== 1) return;
    var r = el.getBoundingClientRect(), o = offsetOf(el);
    box.style.display = 'block';
    box.style.left = (o[0] + r.left) + 'px'; box.style.top = (o[1] + r.top) + 'px';
    box.style.width = r.width + 'px'; box.style.height = r.height + 'px';
  }
  function swallow(e) { e.preventDefault(); e.stopPropagation(); }
  function onClick(e) {
    swallow(e);
    var el = e.target;
    if (!el || el.nodeType !== 1) return;
    top.__atPick = JSON.stringify({ selector: selectorFor(el), frame: framePath(el) });
    cleanup();
  }
  function onKey(e) {
    if (e.key === 'Escape') { swallow(e); top.__atPick = JSON.stringify({ cancelled: true }); cleanup(); }
  }
  function hook(doc) {
    doc.addEventListener('mousemove', onMove, true);
    doc.addEventListener('mousedown', swallow, true);
    doc.addEventListener('mouseup', swallow, true);
    doc.addEventListener('pointerdown', swallow, true);
    doc.addEventListener('click', onClick, true);
    doc.addEventListener('keydown', onKey, true);
    hooked.push(doc);
    Array.prototype.forEach.call(doc.querySelectorAll('iframe,frame'), function (f) {
      try { if (f.contentDocument) hook(f.contentDocument); } catch (e) { }
    });
  }
  function cleanup() {
    hooked.forEach(function (doc) {
      try {
        doc.removeEventListener('mousemove', onMove, true);
        doc.removeEventListener('mousedown', swallow, true);
        doc.removeEventListener('mouseup', swallow, true);
        doc.removeEventListener('pointerdown', swallow, true);
        doc.removeEventListener('click', onClick, true);
        doc.removeEventListener('keydown', onKey, true);
      } catch (e) { }
    });
    hooked = [];
    box.remove(); tip.remove();
    top.__atPickCleanup = null;
  }
  top.__atPickCleanup = cleanup;
  hook(document);
  return true;
})()";

        /// <summary>Reads (and clears) the result; null while the user has not picked yet.</summary>
        public const string Read = "(function(){var r=window.__atPick;return r||null;})()";

        /// <summary>Removes the overlay when the pick is given up (timeout or the program cancelled it).</summary>
        public const string Cancel = "(function(){if(window.__atPickCleanup)window.__atPickCleanup();window.__atPick=null;return true;})()";
    }
}

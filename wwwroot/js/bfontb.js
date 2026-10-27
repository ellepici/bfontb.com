// Ponte minimo tra il C# (Blazor WebAssembly) e le API del browser che il C# non può usare da solo:
// scelta di una cartella (con i percorsi relativi), trascinamento, FontFace, IntersectionObserver,
// localStorage e IndexedDB. Tutta la logica dell'app è in C# (cartella CSharp/).

const files = [];             // funzioni che restituiscono il File; l'indice è l'id usato dal C#
const pendingBytes = new Map();
let app = null;               // DotNetObjectReference di FontLibrary

export function init(dotnetRef, input) {
  app = dotnetRef;
  routerInit();
  // Esc fa "indietro": il pulsante con la freccia nella barra del percorso (impostazioni, famiglia, stile).
  // Non quando serve a un campo, a una tendina, al menu o ai crediti aperti.
  document.addEventListener('keydown', e => {
    if (e.key !== 'Escape' || e.defaultPrevented) return;
    if (e.target.closest && e.target.closest('input, textarea, select, .dd, .menu')) return;
    if (document.querySelector('.menu-pop, .cred-pop, .cm-pop')) return;
    document.querySelector('.crumbs .back')?.click();
  });
  input.addEventListener('change', () => {
    const list = [...input.files];
    input.value = '';
    deliver(list.map(f => [f.webkitRelativePath || f.name, () => Promise.resolve(f), f.size]));
  });
  const root = document.documentElement;
  ['dragenter', 'dragover'].forEach(t => document.addEventListener(t, e => { e.preventDefault(); root.classList.add('drag'); }));
  document.addEventListener('dragleave', e => { if (!e.relatedTarget) root.classList.remove('drag'); });
  document.addEventListener('drop', async e => {
    e.preventDefault(); root.classList.remove('drag');
    const items = [...(e.dataTransfer?.items || [])].map(i => i.webkitGetAsEntry && i.webkitGetAsEntry()).filter(Boolean);
    if (!items.length) return;
    const out = [];
    const walk = async (entry, prefix) => {
      if (entry.isFile) out.push([prefix + entry.name, () => new Promise((ok, ko) => entry.file(ok, ko)), 0]);
      else if (entry.isDirectory) {
        const reader = entry.createReader();
        let batch;
        do {
          batch = await new Promise((ok, ko) => reader.readEntries(ok, ko));
          for (const c of batch) await walk(c, prefix + entry.name + '/');
        } while (batch.length);
      }
    };
    for (const it of items) await walk(it, it.isDirectory ? '' : 'Folder/');
    deliver(out);
  });
}

async function deliver(entries) {
  files.length = 0; pendingBytes.clear();
  const meta = entries.map(([path, get, size], i) => { files[i] = get; return { id: i, path, size }; });
  await app.invokeMethodAsync('OnFolderListed', meta);
}

// Carica il font nel browser con il nome indicato; se il C# deve leggerne i dati, tiene da parte i byte.
export async function loadFace(id, faceName, keepBytes) {
  const file = await files[id]();
  const buf = await file.arrayBuffer();
  if (keepBytes) pendingBytes.set(id, new Uint8Array(buf));
  try {
    const face = new FontFace(faceName, buf);
    await face.load();
    document.fonts.add(face);
    return true;
  } catch { return false; }
}
export function takeBytes(id) {
  const b = pendingBytes.get(id) || new Uint8Array(0);
  pendingBytes.delete(id);
  return b;
}
export async function readBytes(id) {
  const file = await files[id]();
  return new Uint8Array(await file.arrayBuffer());
}

// Disegna alcuni glifi su una tela invisibile e restituisce, riga per riga, quanti pixel sono pieni.
// Il C# (ShapeProbe) usa questi numeri per capire se il carattere ha le grazie.
export async function probeShape(id, glyphs, size) {
  let face = null;
  try {
    const file = await files[id]();
    face = new FontFace('probe' + id, await file.arrayBuffer());
    await face.load();
    document.fonts.add(face);
    const cv = document.createElement('canvas');
    cv.width = size; cv.height = size;
    const ctx = cv.getContext('2d', { willReadFrequently: true });
    const out = [];
    for (const g of glyphs) {
      ctx.clearRect(0, 0, size, size);
      ctx.font = `${Math.round(size * 0.7)}px "probe${id}"`;
      ctx.textBaseline = 'alphabetic';
      ctx.fillStyle = '#000';
      ctx.fillText(g, Math.round(size * 0.3), Math.round(size * 0.82));
      const d = ctx.getImageData(0, 0, size, size).data;
      for (let y = 0; y < size; y++) {
        let w = 0;
        for (let x = 0; x < size; x++) if (d[(y * size + x) * 4 + 3] > 127) w++;
        out.push(w);
      }
    }
    return out;
  } catch { return []; }
  finally { if (face) document.fonts.delete(face); }
}

// Avvisa il componente C# quando il suo testo entra nella zona vicina allo schermo (si mette in coda),
// quando ne esce (si toglie dalla coda se non è ancora partito) e quando entra ed esce dallo schermo (passa avanti, poi torna indietro).
const watched = new WeakMap();   // elemento -> { ref, obs }
const observers = new Map();     // contenitore che scorre -> { near, seen }
function scrollParent(el) {
  for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
    const y = getComputedStyle(p).overflowY;
    if (y === 'auto' || y === 'scroll') return p;
  }
  return null;
}
function notify(e, method, arg) {
  const w = watched.get(e.target);
  if (w) w.ref.invokeMethodAsync(method, arg).catch(() => { });
}
// Il margine vale per il contenitore che scorre (la colonna dei caratteri), non per la finestra.
function observersFor(root) {
  let o = observers.get(root);
  if (!o) {
    o = {
      near: new IntersectionObserver(es => { for (const e of es) notify(e, 'OnNear', e.isIntersecting); }, { root, rootMargin: '1200px 0px' }),
      seen: new IntersectionObserver(es => { for (const e of es) notify(e, 'OnSeen', e.isIntersecting); }, { root }),
    };
    observers.set(root, o);
  }
  return o;
}
// Chiamato anche quando Virtualize riusa la riga per un altro stile: si riparte da capo, così arrivano subito le notifiche giuste.
export function observe(el, ref) {
  if (!el) return;
  unobserve(el);
  const obs = observersFor(scrollParent(el));
  watched.set(el, { ref, obs });
  obs.near.observe(el); obs.seen.observe(el);
}
export function unobserve(el) {
  if (!el) return;
  const w = watched.get(el);
  if (w) { w.obs.near.unobserve(el); w.obs.seen.unobserve(el); }
  watched.delete(el);
}

// Centra il carattere grande della pagina dello stile (un <text> SVG con centro in x = 500) sul suo disegno.
// Per i caratteri codificati misura l'inchiostro con un canvas. Per le forme alternative (il canvas non sa accendere
// le funzioni OpenType) usa il disegno letto dal file; se non c'è (woff), misura la larghezza con un elemento HTML: Safari, nel testo SVG centrato, calcola
// la larghezza delle legature sulle lettere di partenza e le sposta a sinistra; in HTML le compone bene.
export function centerGlyph(el, text, fontPrefix, family, size, feature, inkCenter) {
  if (!el) return;
  el.classList.remove('placed');
  el.style.marginLeft = el.style.marginTop = '0';
  try {
    const fs = parseFloat(getComputedStyle(el).fontSize);
    if (!(fs > 0)) return;
    const box = el.getBoundingClientRect();
    // linea di base: dove sta l'elemento alto zero messo in fondo al testo
    const bl = el.querySelector('.gx-bl');
    const base = bl ? (bl.getBoundingClientRect().top - box.top) / fs : 0.8;
    // centro: sul disegno; per le forme alternative (il canvas non applica le funzioni OpenType) sulla larghezza
    // (il disegno parte dopo il margine interno di .gx-char; la larghezza del riquadro lo comprende già da entrambi i lati)
    // (inkCenter: il centro del disegno in em, letto dal file da C#; c'è per le forme alternative dei file OTF e TTF)
    const ink = feature ? null : inkBox(text, fontPrefix, family);
    const padLeft = parseFloat(getComputedStyle(el).paddingLeft) / fs || 0;
    const center = typeof inkCenter === 'number' ? padLeft + inkCenter : ink ? padLeft + ink.center / InkSize : box.width / 2 / fs;
    el.style.marginLeft = `${(-center).toFixed(4)}em`;
    el.style.marginTop = `${(-base).toFixed(4)}em`;
  } catch { }
  finally { el.classList.add('placed'); }
}

// Dove sta davvero il disegno di un testo, rispetto al punto da cui parte: lo disegniamo su un canvas nascosto
// e cerchiamo i pixel pieni. Le misure del canvas (actualBoundingBox) non bastano: Safari non sempre le calcola sul disegno.
// Risultato per un corpo di InkSize px: center = distanza del centro del disegno dal punto di partenza, width = larghezza teorica.
const InkSize = 200;
const inkCache = new Map();
let inkCtx = null;
function inkBox(text, fontPrefix, family) {
  const key = `${fontPrefix}|${family}|${text}`;
  if (inkCache.has(key)) return inkCache.get(key);
  let res = null;
  try {
    const W = InkSize * 6, H = InkSize * 3, ox = InkSize * 2, oy = InkSize * 2;   // spazio anche a sinistra: gli accenti escono dal loro punto di partenza
    if (!inkCtx) { const c = document.createElement('canvas'); c.width = W; c.height = H; inkCtx = c.getContext('2d', { willReadFrequently: true }); }
    const ctx = inkCtx;
    ctx.clearRect(0, 0, W, H);
    ctx.font = `${fontPrefix} ${InkSize}px ${family}`;
    ctx.textBaseline = 'alphabetic';
    ctx.fillStyle = '#000';
    ctx.fillText(text, ox, oy);
    const d = ctx.getImageData(0, 0, W, H).data;
    let min = -1, max = -1;
    for (let x = 0; x < W && min < 0; x++) for (let y = 0; y < H; y++) if (d[(y * W + x) * 4 + 3] > 24) { min = x; break; }
    for (let x = W - 1; x >= 0 && max < 0; x--) for (let y = 0; y < H; y++) if (d[(y * W + x) * 4 + 3] > 24) { max = x; break; }
    if (min >= 0) res = { center: (min + max + 1) / 2 - ox, width: ctx.measureText(text).width };
  } catch { res = null; }
  if (inkCache.size > 4000) inkCache.clear();
  inkCache.set(key, res);
  return res;
}

// Indice delle impostazioni: un clic scorre alla sezione; scorrendo si accende la sezione che si sta leggendo.
// Le voci senza sezione nella pagina (es. "Proposed families" prima dell'analisi) si nascondono.
let navSpy = null;
export function settingsNav(nav, active, goTo) {
  stopSettingsNav();
  if (!nav) return;
  const links = [...nav.querySelectorAll('[data-target]')];
  // fuori dalle impostazioni le sezioni restano tutte visibili e spente: un clic apre le impostazioni (lo fa Blazor)
  if (!active) { for (const b of links) { b.parentElement.hidden = false; b.classList.remove('on'); } return; }
  const root = document.querySelector('main > .view');
  if (!root) return;
  let queued = false, pinned = null;   // la voce cliccata resta accesa finché non si scorre a mano
  const update = () => {
    queued = false;
    const line = root.getBoundingClientRect().top + 40;
    let current = null, first = null, lastVisible = null;
    for (const b of links) {
      const t = document.getElementById(b.dataset.target);
      b.parentElement.hidden = !t;
      if (!t) continue;
      first ||= b; lastVisible = b;
      if (t.getBoundingClientRect().top <= line) current = b;   // l'ultima sezione già salita sopra la linea
    }
    current ||= first;
    if (root.scrollTop + root.clientHeight >= root.scrollHeight - 2) current = lastVisible;   // in fondo: l'ultima
    if (pinned && !pinned.parentElement.hidden) current = pinned;
    for (const b of links) b.classList.toggle('on', b === current);
  };
  const queue = () => { if (!queued) { queued = true; requestAnimationFrame(update); } };
  const click = e => {
    const b = e.target.closest('[data-target]'); if (!b) return;
    const t = document.getElementById(b.dataset.target);
    if (t) { pinned = b; t.scrollIntoView({ behavior: 'smooth', block: 'start' }); queue(); }
  };
  const unpin = () => { if (pinned) { pinned = null; queue(); } };
  root.addEventListener('scroll', queue, { passive: true });
  ['wheel', 'touchstart', 'keydown', 'mousedown'].forEach(t => root.addEventListener(t, unpin, { passive: true }));
  nav.addEventListener('click', click);
  const mo = new MutationObserver(queue); mo.observe(root, { childList: true, subtree: true });
  update();
  // arrivando da un'altra pagina si scende subito alla sezione scelta (si aspetta qualche fotogramma che compaia)
  if (goTo) {
    const b = links.find(x => x.dataset.target === goTo);
    let tries = 30;
    const jump = () => {
      const t = document.getElementById(goTo);
      if (t) { pinned = b || null; t.scrollIntoView({ block: 'start' }); queue(); }
      else if (--tries > 0) requestAnimationFrame(jump);
    };
    jump();
  }
  navSpy = () => {
    root.removeEventListener('scroll', queue); nav.removeEventListener('click', click); mo.disconnect();
    ['wheel', 'touchstart', 'keydown', 'mousedown'].forEach(t => root.removeEventListener(t, unpin));
  };
}
export function stopSettingsNav() { if (navSpy) { navSpy(); navSpy = null; } }

// Segnalazioni e suggerimenti: il modulo va a Web3Forms (https://web3forms.com), che lo inoltra per email.
// Con withInfo si aggiungono browser e dimensione della finestra.
export async function sendForm(accessKey, form, withInfo) {
  const data = { access_key: accessKey, ...form };
  if (!data.email) delete data.email;
  if (withInfo) data.message += `Browser: ${navigator.userAgent}\nWindow: ${innerWidth}×${innerHeight}\n`;
  try {
    const r = await fetch('https://api.web3forms.com/submit', {
      method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify(data),
    });
    const j = await r.json().catch(() => ({}));
    return { ok: r.ok && j.success !== false, message: j.message || `${r.status} ${r.statusText}` };
  } catch (e) {
    return { ok: false, message: 'no connection' };
  }
}

// Numeri animati (NumberFlow, https://number-flow.barvian.me): la libreria, in js/vendor, si carica la prima volta che serve.
// Se non si carica, il numero si scrive e basta.
let numberFlow = null;
export async function flowNumber(el, value) {
  if (!el) return;
  try {
    numberFlow ||= import('./vendor/number-flow.js').then(() => customElements.whenDefined('number-flow'));
    await numberFlow;
    if (!el.nfReady) {
      el.locales = 'en-US';
      el.spinTiming = { duration: 700, easing: 'cubic-bezier(.2, .8, .2, 1)' };
      el.nfReady = true;
    }
    el.update(value);
  } catch {
    el.textContent = Number(value).toLocaleString('en-US');
  }
}

// Copia un testo negli appunti (il prompt della guida); se il browser non lo permette, con il vecchio metodo della selezione
export async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; } catch { }
  try {
    const ta = document.createElement('textarea');
    ta.value = text; ta.style.cssText = 'position:fixed;left:-9999px;top:0';
    document.body.appendChild(ta); ta.select();
    const ok = document.execCommand('copy'); ta.remove(); return ok;
  } catch { return false; }
}

// Tendina appena aperta: se a destra non c'è posto (il tracking è vicino al bordo), l'elenco si allinea al bordo destro del campo
export function placeList(list) {
  if (!list) return;
  list.style.left = ''; list.style.right = '';
  if (list.getBoundingClientRect().right > document.documentElement.clientWidth - 8) { list.style.left = 'auto'; list.style.right = '0'; }
}

// Tendina aperta: fa scorrere l'elenco quel tanto che basta perché si veda la scelta attiva
export function revealOption(list, i) {
  const el = list && list.children[i];
  if (!el) return;
  const top = el.offsetTop, bottom = top + el.offsetHeight;
  if (top < list.scrollTop) list.scrollTop = top - 4;
  else if (bottom > list.scrollTop + list.clientHeight) list.scrollTop = bottom - list.clientHeight + 4;
}

// Avvisa il C# quando cambia la larghezza di un elemento (la griglia del catalogo calcola quante schede per riga).
const widthWatchers = new WeakMap();
export function watchWidth(el, ref) {
  if (!el || widthWatchers.has(el)) return;
  let last = -1;
  const ro = new ResizeObserver(es => {
    const w = Math.round(es[0].contentRect.width);
    if (w !== last) { last = w; ref.invokeMethodAsync('OnWidth', w).catch(() => { }); }
  });
  ro.observe(el);
  widthWatchers.set(el, ro);
}
export function unwatchWidth(el) { const ro = el && widthWatchers.get(el); if (ro) { ro.disconnect(); widthWatchers.delete(el); } }

// Aspetto chiaro o scuro: i colori sono in tokens.css (:root[data-theme=…]); "auto" segue il sistema.
export function setTheme(theme) {
  const root = document.documentElement;
  if (theme === 'light' || theme === 'dark') root.dataset.theme = theme; else delete root.dataset.theme;
}

// Porta in vista una sezione (la variante scelta nell'elenco a sinistra), sotto il percorso che resta fermo.
export function scrollToId(id) { const el = document.getElementById(id); if (el) el.scrollIntoView({ block: 'start' }); }
// Griglia dei caratteri, frecce su e giù: la cella sopra o sotto quella scelta (la più vicina in orizzontale nella
// riga accanto, anche nel blocco seguente o precedente). La si sceglie con un clic, come farebbe il mouse.
export function glyphKeys(root) {
  if (!root || root.dataset.keys) return;
  root.dataset.keys = '1';
  root.addEventListener('keydown', e => {
    if ((e.key !== 'ArrowUp' && e.key !== 'ArrowDown') || e.altKey || e.metaKey || e.ctrlKey) return;
    const cur = root.querySelector('.gc.on');
    if (!cur) return;
    e.preventDefault();
    const r = cur.getBoundingClientRect(), cx = r.left + r.width / 2, down = e.key === 'ArrowDown';
    let best = null, bestRow = Infinity, bestDx = Infinity;
    for (const c of root.querySelectorAll('.gc')) {
      const b = c.getBoundingClientRect();
      const dy = down ? b.top - r.bottom : r.top - b.bottom;   // le celle della stessa riga hanno dy negativo
      if (dy < -2) continue;
      const dx = Math.abs(b.left + b.width / 2 - cx);
      if (dy < bestRow - 2 || (Math.abs(dy - bestRow) <= 2 && dx < bestDx)) { best = c; bestRow = dy; bestDx = dx; }
    }
    if (!best) return;
    best.click();
    best.scrollIntoView({ block: 'nearest' });
  });
}
// Le frecce funzionano appena si apre la pagina dello stile, senza prima cliccare un carattere: se lo stato attivo non è
// in un campo, in una tendina o nella griglia stessa, la freccia si gira alla griglia (che la tratta come sempre).
document.addEventListener('keydown', e => {
  if (!e.key.startsWith('Arrow') || e.altKey || e.metaKey || e.ctrlKey || e.defaultPrevented) return;
  const gx = document.querySelector('.gx');
  if (!gx || gx.contains(e.target)) return;
  if (e.target.closest?.('input, textarea, select, [contenteditable], .dd, .menu, [role=listbox]') || document.querySelector('.dd.open, .menu-pop')) return;
  e.preventDefault();
  gx.focus({ preventScroll: true });
  gx.dispatchEvent(new KeyboardEvent('keydown', { key: e.key, bubbles: true, cancelable: true }));
});
export function revealGlyph(root) { root?.querySelector('.gc.on')?.scrollIntoView({ block: 'nearest' }); }

// Indice delle famiglie a sinistra: la famiglia aperta sale in cima (con sotto le sue varianti)
export function revealFamily() {
  const list = document.querySelector('.fam-list');
  const li = list?.querySelector(':scope > li > button[aria-current="true"]')?.parentElement;
  if (!li) return;
  const top = list.scrollTop + li.getBoundingClientRect().top - list.getBoundingClientRect().top - 2;
  // da vicino scorre morbido, da lontano salta: un'animazione lunga migliaia di pixel farebbe solo aspettare
  const far = Math.abs(top - list.scrollTop) > list.clientHeight * 1.5;
  list.scrollTo({ top: Math.max(0, top), behavior: far ? 'auto' : 'smooth' });
}
// due fotogrammi dopo: prima la vista nuova torna in cima (showView), poi si scende alla sezione
export function scrollToIdSoon(id) { requestAnimationFrame(() => requestAnimationFrame(() => scrollToId(id))); }

// Celle della griglia con un segno combinante (accento da solo, appoggiato a uno spazio o al cerchio tratteggiato):
// il segno è disegnato fuori dalla sua larghezza, quindi lo spostiamo perché sia il disegno a stare al centro della cella.
export function centerMarks(root, fontPrefix, family) {
  if (!root) return;
  for (const span of root.querySelectorAll('.gc .mk')) {
    const ink = inkBox(span.textContent, fontPrefix, family);
    if (!ink) continue;
    const size = parseFloat(getComputedStyle(span.parentElement).fontSize) || 21;
    // la cella centra la larghezza teorica dell'accento (di solito zero): spostiamo di quanto il disegno ne è lontano
    span.style.transform = `translateX(${((ink.width / 2 - ink.center) * size / InkSize).toFixed(1)}px)`;
  }
}

// Riporta in cima la colonna dei caratteri quando cambia vista, famiglia, ricerca o filtro.
// ---- Indirizzi delle viste (/app, /app/family/…): cambia l'indirizzo, la pagina non si ricarica ----
// Si tiene l'elenco degli indirizzi di questa scheda (sopravvive a un ricaricamento in sessionStorage): quando si torna
// alla vista appena precedente ("Back to the catalog", Esc, il secondo clic su una famiglia) si fa Indietro invece di
// aggiungere un passo, così la cronologia del browser resta pulita.
let hist = { stack: [location.pathname], pos: 0 };
function histSave() { try { sessionStorage.setItem('pf.hist', JSON.stringify(hist)); } catch { } }
function routerInit() {
  try { const saved = JSON.parse(sessionStorage.getItem('pf.hist')); if (saved?.stack) hist = saved; } catch { }
  const pos = history.state?.i;
  if (typeof pos === 'number' && hist.stack[pos] === location.pathname) hist.pos = pos;
  else { hist = { stack: [location.pathname], pos: 0 }; history.replaceState({ i: 0 }, '', location.href); }
  histSave();
  addEventListener('popstate', e => {
    hist.pos = typeof e.state?.i === 'number' ? e.state.i : 0;
    hist.stack[hist.pos] = location.pathname;
    histSave();
    app?.invokeMethodAsync('OnUrl', location.pathname);
  });
}
export function goUrl(url, title, replace) {
  document.title = title;
  if (url === location.pathname) return;
  if (!replace && hist.pos > 0 && hist.stack[hist.pos - 1] === url) { history.back(); return; }
  if (replace) history.replaceState({ i: hist.pos }, '', url);
  else { hist.stack = hist.stack.slice(0, hist.pos + 1); hist.pos++; history.pushState({ i: hist.pos }, '', url); }
  hist.stack[hist.pos] = url;
  histSave();
}
export function setTitle(title) { document.title = title; }

// Dove si era nella colonna dei caratteri, per ognuna delle cose che mostra (data-key: vista, filtri, ricerca…).
// Si ricorda mentre si scorre, in pixel (l'altezza dello scorrimento virtuale cambia di continuo, una frazione sbaglierebbe).
// Lo scorrimento virtuale del catalogo è già alto quanto tutte le righe, quindi si può saltare subito al punto giusto.
const viewPos = new Map();
document.addEventListener('scroll', e => {
  const el = e.target;
  if (el instanceof Element && el.matches('main > .view') && el.dataset.key) viewPos.set(el.dataset.key, el.scrollTop);
}, { capture: true, passive: true });
let viewRestore = 0;
export function showView(key, restore) {
  const el = document.querySelector('main > .view');
  if (!el) return;
  const at = viewPos.get(key);
  const run = ++viewRestore;
  if (!restore || at === undefined) { el.scrollTop = 0; return; }
  // l'altezza dell'elenco si assesta nei primi fotogrammi: si rimette la posizione finché non resta ferma
  // (o finché non si scorre a mano)
  let lastH = -1, still = 0, frames = 0;
  const stop = () => { viewRestore++; };
  ['wheel', 'touchstart', 'keydown', 'mousedown'].forEach(t => el.addEventListener(t, stop, { once: true, passive: true }));
  const step = () => {
    if (run !== viewRestore) return;
    el.scrollTop = at;
    still = el.scrollHeight === lastH ? still + 1 : 0;
    lastH = el.scrollHeight;
    if (still < 4 && ++frames < 60) requestAnimationFrame(step);
  };
  step();
}

export function scrollTop(selector) { const el = document.querySelector(selector); if (el) el.scrollTop = 0; }

// localStorage (può non essere disponibile: in quel caso non si salva nulla)
export function lsGet(key) { try { return localStorage.getItem(key); } catch { return null; } }
export function lsSet(key, value) { try { localStorage.setItem(key, value); } catch { } }

// IndexedDB: cache dei risultati dell'analisi (chiave = percorso, valore = JSON prodotto dal C#)
let dbp = null;
function db() {
  if (!dbp) dbp = new Promise(ok => {
    try {
      const r = indexedDB.open('banco-di-prova-font', 2);
      r.onupgradeneeded = () => {
        if (!r.result.objectStoreNames.contains('info')) r.result.createObjectStore('info');
        if (!r.result.objectStoreNames.contains('meta')) r.result.createObjectStore('meta');
      };
      r.onsuccess = () => ok(r.result);
      r.onerror = () => ok(null);
    } catch { ok(null); }
  });
  return dbp;
}
export async function idbLoad(prefix) {
  const d = await db(); if (!d) return [];
  return new Promise(ok => {
    const out = [];
    try {
      const req = d.transaction('info').objectStore('info').openCursor(IDBKeyRange.bound(prefix, prefix + '￿'));
      req.onsuccess = () => { const c = req.result; if (c) { out.push([c.key, c.value]); c.continue(); } else ok(out); };
      req.onerror = () => ok(out);
    } catch { ok(out); }
  });
}
export async function idbPut(pairs) {
  const d = await db(); if (!d) return;
  try { const st = d.transaction('info', 'readwrite').objectStore('info'); for (const [k, v] of pairs) st.put(v, k); } catch { }
}
export async function metaGet(key) {
  const d = await db(); if (!d) return null;
  return new Promise(ok => {
    try {
      const req = d.transaction('meta').objectStore('meta').get(key);
      req.onsuccess = () => ok(req.result ?? null);
      req.onerror = () => ok(null);
    } catch { ok(null); }
  });
}
export async function metaSet(key, value) {
  const d = await db(); if (!d) return;
  try { d.transaction('meta', 'readwrite').objectStore('meta').put(value, key); } catch { }
}
export async function idbClear(prefix) {
  const d = await db(); if (!d) return;
  try { d.transaction('info', 'readwrite').objectStore('info').delete(IDBKeyRange.bound(prefix, prefix + '￿')); } catch { }
}

// Campioni dell'elenco limitati a due righe (vedi specimens.css): si tagliano solo quelli che vanno oltre.
// Si ricontrolla quando cambia qualcosa nella pagina (testo, corpo, font caricato, righe nuove) e quando cambia la finestra.
function fitSpecs() {
  for (const el of document.querySelectorAll('.catalog-list .spec')) {
    const over = el.scrollHeight > el.clientHeight + 2;
    if (el.classList.contains('clamped') !== over) el.classList.toggle('clamped', over);
  }
}
let fitQueued = false;
function queueFit() { if (fitQueued) return; fitQueued = true; requestAnimationFrame(() => { fitQueued = false; fitSpecs(); }); }
if (typeof document !== 'undefined') {
  new MutationObserver(queueFit).observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['class', 'style'] });
  addEventListener('resize', queueFit);
  document.fonts?.addEventListener?.('loadingdone', queueFit);
}

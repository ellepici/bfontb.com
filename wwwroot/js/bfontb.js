// Ponte minimo tra il C# (Blazor WebAssembly) e le API del browser che il C# non può usare da solo:
// scelta di una cartella (con i percorsi relativi), trascinamento, FontFace, IntersectionObserver,
// localStorage e IndexedDB. Tutta la logica dell'app è in C# (cartella CSharp/).

const files = [];             // funzioni che restituiscono il File; l'indice è l'id usato dal C#
const pendingBytes = new Map();
let app = null;               // DotNetObjectReference di FontLibrary

export function init(dotnetRef, input) {
  app = dotnetRef;
  routerInit();
  mergedPops();
  // Esc fa "indietro": il pulsante con la freccia nella barra del percorso (impostazioni, famiglia, stile).
  // Non quando serve a un campo, a una tendina, al menu o ai crediti aperti.
  document.addEventListener('keydown', e => {
    if (e.key !== 'Escape' || e.defaultPrevented) return;
    // crediti aperti e attenzione altrove nella pagina: Esc li chiude, come le tendine (dentro il riquadro ci pensa StyleView)
    const cred = document.querySelector('.cred-pop');
    if (cred && !(e.target.closest && e.target.closest('.cred-dd'))) { cred.parentElement.querySelector('.cred-btn')?.click(); return; }
    if (e.target.closest && e.target.closest('input, textarea, select, .dd, .menu, .cm, .fold, .cred-dd')) return;
    if (document.querySelector('.menu-pop, .cred-pop, .cm-pop, .fold-pop')) return;
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

// ---- barre HSL dei colori (ColorMenu): tinta 0–360, saturazione e luminosità 0–100, ognuna con il suo valore da scrivere ----
// Si trascinano con il mouse (o il dito) e si regolano con le frecce (Shift: passi da 10); il colore va a C# (OnWheel)
// al massimo ogni 60 ms mentre si trascina, e sempre alla fine, così il resto della pagina non si ridisegna a ogni pixel.
const hexToHsl = hex => {
  const n = parseInt(hex.slice(1), 16), r = (n >> 16 & 255) / 255, g = (n >> 8 & 255) / 255, b = (n & 255) / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min, l = (max + min) / 2;
  let h = 0;
  if (d) h = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  const s = d ? d / (1 - Math.abs(2 * l - 1)) : 0;
  return { h: Math.round((h * 60 + 360) % 360), s: Math.round(s * 100), l: Math.round(l * 100) };
};
const hslToHex = (h, s, l) => {
  s /= 100; l /= 100;
  const a = s * Math.min(l, 1 - l), f = n => { const k = (n + h / 30) % 12; return l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)); };
  return '#' + [f(0), f(8), f(4)].map(x => Math.round(x * 255).toString(16).padStart(2, '0')).join('').toUpperCase();
};
// Un colore del tema (variabile CSS) in esadecimale: serve a ColorMenu per mostrare il colore "auto"
// "#a1b2c3", "#abc" o "rgb(161, 178, 195)" → "#A1B2C3"; null se non è un colore
function toHex(v) {
  v = String(v || '').trim();
  let m = v.match(/^#([0-9a-f]{6})/i);
  if (m) return '#' + m[1].toUpperCase();
  m = v.match(/^#([0-9a-f])([0-9a-f])([0-9a-f])$/i);
  if (m) return '#' + (m[1] + m[1] + m[2] + m[2] + m[3] + m[3]).toUpperCase();
  m = v.match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)/i);
  return m ? '#' + [m[1], m[2], m[3]].map(x => (+x).toString(16).padStart(2, '0')).join('').toUpperCase() : null;
}

export function cssColor(name) {
  const v = getComputedStyle(document.querySelector('.app') || document.documentElement).getPropertyValue(name).trim();
  if (/^#[0-9a-f]{3,8}$/i.test(v)) return v.slice(0, 7).toUpperCase();
  const m = v.match(/rgba?\(\s*(\d+)[,\s]+(\d+)[,\s]+(\d+)/i);
  return m ? '#' + [m[1], m[2], m[3]].map(x => (+x).toString(16).padStart(2, '0')).join('').toUpperCase() : null;
}

// I campi del codice dei colori mentre li si scrive. Esadecimale: solo cifre esadecimali, maiuscole, al massimo sei;
// il # davanti è già disegnato fuori dal campo: se lo si scrive o lo si incolla ("#ff00aa") si toglie da solo.
// R, G, B: solo cifre, da 0 a 255 (un 300 diventa 255).
const isCode = el => el instanceof HTMLInputElement && (el.classList.contains('cm-hex') || el.classList.contains('cm-ch'));
document.addEventListener('input', e => {
  const el = e.target;
  if (!isCode(el)) return;
  const hex = el.classList.contains('cm-hex');
  const before = el.value, caret = el.selectionStart ?? before.length;
  const clean = s => hex ? s.replace(/[^0-9a-f]/gi, '').toUpperCase() : s.replace(/\D/g, '');
  let v = clean(before).slice(0, hex ? 6 : 3);
  if (!hex && v.length > 0) v = String(Math.min(255, parseInt(v, 10)));
  if (v === before) return;
  const pos = Math.min(clean(before.slice(0, caret)).length, v.length);
  el.value = v;
  el.setSelectionRange(pos, pos);
  el.dataset.fixed = '1';   // Safari, dopo un valore cambiato da qui, non manda più "change": lo mandiamo noi (sotto)
}, true);
const commitCode = el => {
  if (!isCode(el) || !el.dataset.fixed) return;
  delete el.dataset.fixed;
  el.dispatchEvent(new Event('change', { bubbles: true }));
};
document.addEventListener('keydown', e => {
  const el = e.target;
  if (e.key === 'Enter') { commitCode(el); return; }
  // nei campi R, G, B le frecce su e giù cambiano il valore di 1 (Shift: 10) e lo applicano subito
  if ((e.key !== 'ArrowUp' && e.key !== 'ArrowDown') || !(el instanceof HTMLInputElement) || !el.classList.contains('cm-ch')) return;
  e.preventDefault(); e.stopPropagation();
  const step = (e.shiftKey ? 10 : 1) * (e.key === 'ArrowUp' ? 1 : -1);
  el.value = String(Math.min(255, Math.max(0, (parseInt(el.value, 10) || 0) + step)));
  delete el.dataset.fixed;
  el.dispatchEvent(new Event('change', { bubbles: true }));
}, true);
document.addEventListener('blur', e => commitCode(e.target), true);
// un clic in un campo del codice ne seleziona tutto il contenuto, pronto da riscrivere (dopo il clic: Safari altrimenti la toglie)
document.addEventListener('focus', e => { if (isCode(e.target)) setTimeout(() => e.target.select(), 0); }, true);

export function colorWheel(root, dotnet, hex, isInk) {
  if (!root) return;
  const c = hexToHsl(hex);
  const max = { h: 360, s: 100, l: 100 };
  const bars = [...root.querySelectorAll('.cw-bar')];
  let last = 0, timer = 0;
  // il pomello non esce dalla barra: il suo centro va da metà altezza a sinistra a metà altezza a destra, e la sfumatura
  // si stende sullo stesso tratto (sotto il pomello c'è sempre il suo colore)
  const inset = bar => bar.clientHeight / 2;
  const grad = (bar, stops) => {
    const r = inset(bar), n = stops.length - 1;
    return `linear-gradient(to right, ${stops.map((s, i) => `${s} calc(${r}px + (100% - ${2 * r}px) * ${i / n})`).join(', ')})`;
  };
  const draw = () => {
    const { h, s, l } = c;
    for (const bar of bars) {
      const k = bar.dataset.k;
      bar.style.background = grad(bar, k === 'h' ? [0, 60, 120, 180, 240, 300, 360].map(x => `hsl(${x} 100% 50%)`)
        : k === 's' ? [`hsl(${h} 0% ${l}%)`, `hsl(${h} 100% ${l}%)`]
        : ['#000', `hsl(${h} ${s}% 50%)`, '#fff']);
      const r = inset(bar);
      const knob = bar.querySelector('.cw-knob');
      knob.style.left = `calc(${r}px + (100% - ${2 * r}px) * ${c[k] / max[k]})`;
      knob.style.background = hslToHex(h, s, l);
      bar.setAttribute('aria-valuenow', String(c[k]));
      const box = bar.parentElement.querySelector('.cw-val');
      if (box && document.activeElement !== box) box.value = String(c[k]);   // mentre ci si scrive non si cambia sotto le dita
    }
  };
  // exact: un colore preso dal contagocce va mandato così com'è (passando da HSL, arrotondato, cambierebbe di poco)
  let exact = null;
  const send = (final, hex) => {
    if (hex !== undefined) exact = hex;
    clearTimeout(timer);
    const now = performance.now();
    if (!final && now - last < 60) { timer = setTimeout(() => send(true), 60); return; }
    last = now;
    dotnet.invokeMethodAsync('OnWheel', exact ?? hslToHex(c.h, c.s, c.l), isInk).catch(() => { });
    exact = null;
  };
  // il contagocce: un colore preso dallo schermo. Dove c'è (Chrome, Edge) si usa EyeDropper; Safari non ce l'ha (vedi sotto)
  const take = (v, final) => {
    const h = toHex(v); if (!h) return;
    Object.assign(c, hexToHsl(h)); draw(); send(final, h);
  };
  // il clic arriva direttamente sul campo colore trasparente sopra l'icona (ColorMenu.razor). Dove c'è EyeDropper quel
  // clic si ferma e si apre il contagocce del browser; altrimenti (Safari) si apre il selettore di macOS, il cui pannello
  // ("Mostra colori…") ha la lente per prendere un colore da tutto lo schermo
  const eye = root.closest('.cm-pop')?.querySelector('.cm-eye-in');
  if (eye) {
    eye.onpointerdown = eye.onfocus = () => { eye.value = hslToHex(c.h, c.s, c.l).toLowerCase(); };   // parte dal colore attuale
    eye.onclick = e => {
      if (!('EyeDropper' in window)) return;
      e.preventDefault();
      new window.EyeDropper().open().then(r => take(r.sRGBHex, true)).catch(() => { });   // Esc: nessun colore
    };
    eye.oninput = () => take(eye.value, false);   // mentre si sceglie nel pannello, il colore cambia dal vivo
    eye.onchange = () => take(eye.value, true);
  }
  for (const bar of bars) {
    const k = bar.dataset.k;
    const read = e => {
      const rc = bar.getBoundingClientRect(), r = inset(bar);
      c[k] = Math.round(Math.min(1, Math.max(0, (e.clientX - rc.left - r) / Math.max(1, rc.width - 2 * r))) * max[k]);
    };
    bar.onpointerdown = e => {
      e.preventDefault(); bar.focus(); bar.setPointerCapture(e.pointerId);
      read(e); draw(); send(false);
      bar.onpointermove = m => { read(m); draw(); send(false); };
      bar.onpointerup = bar.onpointercancel = () => { bar.onpointermove = null; send(true); };
    };
    // il valore scritto a mano: solo cifre, entro il massimo; si applica con Invio o uscendo dalla casella;
    // le frecce su e giù lo cambiano di 1 (Shift: 10) e lo applicano subito
    const box = bar.parentElement.querySelector('.cw-val');
    if (box) {
      const commit = () => {
        const n = parseInt(box.value, 10);
        if (!Number.isNaN(n)) { c[k] = Math.min(max[k], Math.max(0, n)); send(true); }
        box.value = String(c[k]); draw();
      };
      box.oninput = () => {
        const v = box.value.replace(/\D/g, '').slice(0, 3);
        const fixed = v.length ? String(Math.min(max[k], parseInt(v, 10))) : '';
        if (fixed !== box.value) box.value = fixed;
      };
      box.onchange = commit;
      box.onblur = commit;   // Safari, dopo un valore corretto da qui, non manda "change"
      box.onkeydown = e => {
        if (e.key === 'Enter') { e.preventDefault(); commit(); box.select(); return; }
        if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
        e.preventDefault(); e.stopPropagation();
        const step = e.shiftKey ? 10 : 1;
        c[k] = Math.min(max[k], Math.max(0, (parseInt(box.value, 10) || 0) + (e.key === 'ArrowUp' ? step : -step)));
        box.value = String(c[k]); draw(); send(false);
      };
      box.onfocus = () => setTimeout(() => box.select(), 0);   // dopo il clic, che altrimenti toglie la selezione (Safari)
    }
    // da tastiera: le frecce spostano il pomello di 1 (Shift: 10)
    bar.onkeydown = e => {
      const step = e.shiftKey ? 10 : 1;
      if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') c[k] = Math.max(0, c[k] - step);
      else if (e.key === 'ArrowRight' || e.key === 'ArrowUp') c[k] = Math.min(max[k], c[k] + step);
      else return;
      e.preventDefault(); e.stopPropagation(); draw(); send(false);
    };
  }
  draw();
}

// ---- Tendine "attaccate": il pulsante e il riquadro che scende diventano una forma sola ----
// Un unico contorno SVG (angoli squircle, raccordo concavo dove la linguetta del pulsante incontra il riquadro)
// disegnato sotto pulsante e riquadro, che intanto perdono bordo e fondo (classe "merged" sul contenitore).
// Vale per ogni tendina: [riquadro, contenitore, pulsante]. Funziona da solo: guarda quando un riquadro compare.
const MERGED = [
  ['.dd-list', '.dd', ':scope > .dd-btn, :scope > .dd-field'],
  ['.cm-pop', '.cm', ':scope > .cm-btn'],
  ['.menu-pop', '.menu', ':scope > .menu-btn'],
  ['.fold-pop', '.fold', ':scope > .fold-btn'],
  ['.cred-pop', '.cred-dd', ':scope > .cred-btn'],
];
const SQ_N = 4;            // superellisse come gli squircle dei pulsanti (squircle.css)
const merged = new Map();  // riquadro → { wrap, trig, svg, path, filter, ro }
let mergeIds = 0;          // per dare a ogni ombra un id suo

function mergedPops() {
  // l'osservatore risponde prima che la pagina venga ridisegnata: il riquadro compare già attaccato, senza scatti
  new MutationObserver(syncMerged).observe(document.body, { childList: true, subtree: true });
  window.addEventListener('resize', () => merged.forEach(drawMerged));
}

function syncMerged() {
  // via le forme dei riquadri chiusi
  for (const [pop, m] of merged) {
    if (pop.isConnected) continue;
    m.ro.disconnect(); m.svg.remove(); m.wrap.classList.remove('merged'); merged.delete(pop);
  }
  for (const [popSel, wrapSel, trigSel] of MERGED) {
    for (const pop of document.querySelectorAll(popSel)) {
      if (merged.has(pop)) continue;
      const wrap = pop.parentElement;
      if (!wrap || !wrap.matches(wrapSel)) continue;
      const trig = wrap.querySelector(trigSel);
      if (!trig) continue;
      const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      svg.setAttribute('class', 'merge-shape'); svg.setAttribute('aria-hidden', 'true');
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      // l'ombra della tendina aperta (la stessa dei pulsanti, --btn-shadow): un filtro SVG sul contorno,
      // non un filter CSS sull'svg, che in Safari taglierebbe tutta la forma (l'svg è largo 1px e il disegno sta fuori)
      const fid = 'msh' + (++mergeIds);
      const filter = document.createElementNS('http://www.w3.org/2000/svg', 'filter');
      filter.setAttribute('id', fid); filter.setAttribute('filterUnits', 'userSpaceOnUse');
      filter.setAttribute('color-interpolation-filters', 'sRGB');
      const drop = document.createElementNS('http://www.w3.org/2000/svg', 'feDropShadow');
      drop.setAttribute('dx', '0'); drop.setAttribute('dy', '4'); drop.setAttribute('stdDeviation', '6');
      drop.setAttribute('flood-color', '#000'); drop.setAttribute('flood-opacity', '0.10');
      filter.appendChild(drop);
      const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
      defs.appendChild(filter);
      svg.appendChild(defs);
      path.setAttribute('filter', `url(#${fid})`);
      svg.appendChild(path);
      wrap.classList.add('merged');
      wrap.insertBefore(svg, wrap.firstChild);
      const m = { wrap, trig, pop, svg, path, filter, ro: new ResizeObserver(() => drawMerged(m)) };
      m.ro.observe(pop); m.ro.observe(trig);
      merged.set(pop, m);
      drawMerged(m);
    }
  }
}

function drawMerged(m) {
  const w = m.wrap.getBoundingClientRect();
  const rel = r => ({ l: r.left - w.left, r: r.right - w.left, t: r.top - w.top, b: r.bottom - w.top });
  const t = rel(m.trig.getBoundingClientRect()), p = rel(m.pop.getBoundingClientRect());
  // la tendina dentro il riquadro del testo di prova: la linguetta arriva fino al bordo del riquadro
  if (m.trig.closest('.in-box')) { t.t -= 1; t.l -= 1; t.b += 1; }
  const tabH = t.b - t.t;
  const rT = tabH >= 30 ? 12 : 10, rP = 14, rC = 8;   // raggi: linguetta, riquadro, raccordo concavo
  // se il riquadro si aprisse verso l'alto si lavora a specchio
  const up = p.b <= t.t + tabH / 2;
  const fy = up ? (y => -y) : (y => y);
  const tt = up ? -t.b : t.t, pj = up ? -p.b : p.t, pb = up ? -p.t : p.b;
  // mezzo pixel dentro: il tratto da 1px cade esattamente dove stava il bordo
  const h = 0.5;
  let tl = t.l + h, tr = t.r - h, pl = p.l + h, pr = p.r - h;
  // un riquadro appena più largo del pulsante darebbe un gradino minuscolo: allora è la linguetta ad allargarsi
  if (Math.abs(pl - tl) < 3) pl = tl; else if (tl - pl > 0 && tl - pl < 24) tl = pl;
  if (Math.abs(pr - tr) < 3) pr = tr; else if (pr - tr > 0 && pr - tr < 24) tr = pr;
  const T = tt + h, J = pj, B = pb - h;
  let pts = [[tl, T], [tr, T], [tr, J], [pr, J], [pr, B], [pl, B], [pl, J], [tl, J]];
  // via i punti doppi e quelli in mezzo a un lato dritto
  pts = pts.filter((q, i, a) => { const n = a[(i + 1) % a.length]; return Math.abs(q[0] - n[0]) > .01 || Math.abs(q[1] - n[1]) > .01; });
  pts = pts.filter((q, i, a) => {
    const a0 = a[(i - 1 + a.length) % a.length], a2 = a[(i + 1) % a.length];
    return Math.abs((q[0] - a0[0]) * (a2[1] - q[1]) - (q[1] - a0[1]) * (a2[0] - q[0])) > .01;
  });
  const e = 2 / SQ_N, steps = 12;
  let d = '';
  pts.forEach((v, i) => {
    const a = pts[(i - 1 + pts.length) % pts.length], b = pts[(i + 1) % pts.length];
    const l1 = Math.hypot(v[0] - a[0], v[1] - a[1]), l2 = Math.hypot(b[0] - v[0], b[1] - v[1]);
    const d1 = [(v[0] - a[0]) / l1, (v[1] - a[1]) / l1], d2 = [(b[0] - v[0]) / l2, (b[1] - v[1]) / l2];
    const convex = d1[0] * d2[1] - d1[1] * d2[0] > 0;
    let r = !convex ? rC : (Math.abs(v[1] - T) < .01 ? rT : (Math.abs(v[1] - J) < .01 && (v[0] === tl || v[0] === tr) && !(v[0] === pl || v[0] === pr)) ? rT : rP);
    r = Math.min(r, l1 / 2, l2 / 2);
    const c = [v[0] - d1[0] * r + d2[0] * r, v[1] - d1[1] * r + d2[1] * r];
    for (let k = 0; k <= steps; k++) {
      const th = (k / steps) * Math.PI / 2;
      const cs = Math.pow(Math.cos(th), e), sn = Math.pow(Math.sin(th), e);
      const x = c[0] - d2[0] * r * cs + d1[0] * r * sn, y = c[1] - d2[1] * r * cs + d1[1] * r * sn;
      d += (d ? 'L' : 'M') + x.toFixed(2) + ',' + fy(y).toFixed(2);
    }
  });
  m.path.setAttribute('d', d + 'Z');
  // la zona dell'ombra: tutta la forma con un margine per la sfocatura
  const x0 = Math.min(t.l, p.l) - 40, x1 = Math.max(t.r, p.r) + 40;
  const y0 = Math.min(fy(T), fy(B), fy(J)) - 40, y1 = Math.max(fy(T), fy(B), fy(J)) + 50;
  m.filter.setAttribute('x', x0); m.filter.setAttribute('y', y0);
  m.filter.setAttribute('width', x1 - x0); m.filter.setAttribute('height', y1 - y0);
}

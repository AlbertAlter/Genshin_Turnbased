"use strict";

(() => {
  // Key, preview property, Excel multiplier, unit, editor name, value type.
  const FIELDS = [
    ["CardHeight", "cardHeight", 1, "px", "预览卡牌高度", "number"],
    ["AnchorY", "anchorY", 1, "0/0.5/1", "卡牌下沿/中线/上沿", "number"],
    ["OffsetX", "offsetX", 1, "px", "水平偏移（向右为正）", "number"],
    ["OffsetY", "offsetY", 1, "px", "垂直偏移（向上为正）", "number"],
    ["CardGapHeightRatio", "cardGap", 0.01, "卡高比例", "距锚点间距", "number"],
    ["StackGapHeightRatio", "stackGap", 0.01, "卡高比例", "连续叠放间距", "number"],
    ["RiseHeightRatio", "riseRatio", 0.01, "卡高比例", "匀速上浮距离", "number"],
    ["LifetimeSeconds", "lifetime", 1, "秒", "显示总时长", "number"],
    ["FadeInSeconds", "fadeIn", 1, "秒", "淡入时长", "number"],
    ["FadeOutSeconds", "fadeOut", 1, "秒", "淡出时长", "number"],
    ["MaxVisible", "maxVisible", 1, "条", "最大同时显示", "number"],
    ["SpawnIntervalSeconds", "spawnInterval", 1, "秒", "连续弹出间隔", "number"],
    ["LoopDelaySeconds", "loopDelay", 1, "秒", "循环间隔", "number"],
    ["FontSize", "fontSize", 1, "px", "字号", "number"],
    ["LineHeight", "lineHeight", 1, "倍", "行高", "number"],
    ["MaxWidth", "maxWidth", 1, "px", "最大宽度", "number"],
    ["PaddingX", "paddingX", 1, "px", "左右留白", "number"],
    ["PaddingY", "paddingY", 1, "px", "上下留白", "number"],
    ["BackgroundOpacity", "backgroundOpacity", 0.01, "0-1", "底板不透明度", "number"],
    ["EndFadeRatio", "edgeFade", 0.01, "0-0.45", "每端渐隐长度比例", "number"],
    ["GoldBorderWidth", "goldWidth", 1, "px", "上下金边厚度", "number"],
    ["GoldBorderOpacity", "goldOpacity", 0.01, "0-1", "金边不透明度", "number"],
    ["TextColor", "textColor", 1, "#RRGGBB", "文字颜色", "text"],
    ["BackgroundColor", "backgroundColor", 1, "#RRGGBB", "底板颜色", "text"],
    ["GoldColor", "goldColor", 1, "#RRGGBB", "金边颜色", "text"],
    ["ShowGuides", "guides", 1, "0/1", "显示预览辅助线", "boolean"],
    ["BackgroundOnly", "backgroundOnly", 1, "0/1", "仅显示背景", "boolean"]
  ];
  function mount(options) {
  const FIELDS = options.fields;
  const MAIN = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  const REL = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
  const api = options.api;
  const status = document.querySelector(`#${options.prefix}-status`);
  const connectButton = document.querySelector(`#${options.prefix}-connect`);
  const readButton = document.querySelector(`#${options.prefix}-read`), saveButton = document.querySelector(`#${options.prefix}-save`);
  let handle = null, book = null, busy = false;
  const nodes = (root, tag) => [...root.getElementsByTagNameNS(MAIN, tag)];
  const text = node => nodes(node, "t").map(t => t.textContent).join("");
  const same = (a, b) => typeof a === "number" ? Math.abs(a - b) < 1e-10 : a === b;
  const valuesFor = p => Object.fromEntries(FIELDS.map(([key, prop, factor, , , type]) => [key,
    type === "number" ? Number((p[prop] * factor).toPrecision(14)) : type === "boolean" ? (p[prop] ? 1 : 0) : p[prop].toUpperCase()]));
  function xml(source) {
    const doc = new DOMParser().parseFromString(source, "application/xml");
    if (doc.getElementsByTagName("parsererror").length) throw Error("参数表 XML 无法读取。");
    return doc;
  }
  function internalPath(target) {
    const parts = (target.startsWith("/") ? target.slice(1) : "xl/" + target).split("/"), result = [];
    for (const part of parts) if (part === "..") result.pop(); else if (part && part !== ".") result.push(part);
    return result.join("/");
  }
  async function decode(bytes) {
    const zip = await JSZip.loadAsync(bytes, { checkCRC32: true });
    const part = async name => {
      const file = zip.file(name); if (!file) throw Error("参数表缺少部件：" + name);
      return xml(await file.async("string"));
    };
    const workbook = await part("xl/workbook.xml"), relations = await part("xl/_rels/workbook.xml.rels");
    const sheet = nodes(workbook, "sheet").find(node => node.getAttribute("name") === "Parameters");
    if (!sheet) throw Error("需要包含 Parameters 子表的状态浮层参数表。");
    const id = sheet.getAttributeNS(REL, "id");
    const relation = [...relations.documentElement.children].find(node => node.getAttribute("Id") === id);
    if (!relation || relation.getAttribute("TargetMode") === "External") throw Error("Parameters 子表关联无效。");
    const sheetPath = internalPath(relation.getAttribute("Target")), doc = await part(sheetPath);
    const sst = zip.file("xl/sharedStrings.xml") ? await part("xl/sharedStrings.xml") : null;
    const strings = sst ? nodes(sst, "si").map(text) : [];
    const cellText = cell => {
      if (!cell) return "";
      if (cell.getAttribute("t") === "inlineStr") return text(cell);
      const value = nodes(cell, "v")[0]?.textContent || "";
      return cell.getAttribute("t") === "s" ? strings[Number(value)] ?? "" : value;
    };
    const rows = nodes(doc, "row"); let keyColumn, valueColumn, headerIndex;
    for (let i = 0; i < Math.min(10, rows.length); i++) {
      const columns = Object.fromEntries([...rows[i].children].filter(c => c.localName === "c").map(c => [cellText(c).trim().toLowerCase(), c.getAttribute("r").replace(/\d/g, "")]));
      if (columns.key && columns.value) { keyColumn = columns.key; valueColumn = columns.value; headerIndex = i; break; }
    }
    if (headerIndex === undefined) throw Error("Parameters 需要 Key 和 Value 表头。");
    const cells = new Map(), values = {}, allKeys = new Set();
    for (const row of rows.slice(headerIndex + 1)) {
      const rowCells = [...row.children].filter(c => c.localName === "c");
      const key = cellText(rowCells.find(c => c.getAttribute("r").replace(/\d/g, "") === keyColumn)).trim();
      if (!key) continue;
      if (allKeys.has(key)) throw Error("参数 Key 重复：" + key); allKeys.add(key);
      const field = FIELDS.find(entry => entry[0] === key); if (!field) throw Error("未知参数：" + key);
      const cell = rowCells.find(c => c.getAttribute("r").replace(/\d/g, "") === valueColumn);
      if (!cell || nodes(cell, "f").length) throw Error("参数 Value 不能留空或使用公式：" + key);
      const raw = cellText(cell).trim(); if (!raw) throw Error("参数 Value 不能为空：" + key);
      const value = field[5] === "text" ? raw.toUpperCase() : Number(raw);
      if (typeof value === "number" && !Number.isFinite(value)) throw Error("参数必须为有限数字：" + key);
      if (field[5] === "boolean" && value !== 0 && value !== 1) throw Error("参数必须为0或1：" + key);
      values[key] = value; cells.set(key, cell);
    }
    const preview = {};
    for (const [key, prop, factor, , , type] of FIELDS) {
      if (!(key in values)) throw Error("参数表缺少：" + key);
      preview[prop] = type === "boolean" ? values[key] === 1 : type === "number" ? values[key] / factor : values[key];
    }
    api.validateParameters(preview);
    return { zip, doc, sheetPath, sst, strings, cells, values, preview, bytes: new Uint8Array(bytes) };
  }
  async function readHandle(fileHandle) {
    const file = await fileHandle.getFile();
    if (!/\.xlsx$/i.test(file.name) || file.size > 8 * 1024 * 1024) throw Error("请选择不超过8MB的 xlsx 参数表。");
    return { ...await decode(await file.arrayBuffer()), name: file.name };
  }
  async function encode(current, values) {
    let sharedChanged = false, sharedRefDelta = 0;
    for (const [key, value] of Object.entries(values)) {
      if (same(current.values[key], value)) continue;
      const cell = current.cells.get(key), oldType = cell.getAttribute("t");
      for (const node of [...cell.children]) if (["v", "is", "f"].includes(node.localName)) cell.removeChild(node);
      let child;
      if (typeof value === "number") { if (oldType === "s") sharedRefDelta--; cell.removeAttribute("t"); child = current.doc.createElementNS(MAIN, "v"); child.textContent = String(value); }
      else if (oldType === "s" && current.sst) {
        let index = current.strings.indexOf(value);
        if (index < 0) {
          index = current.strings.length; current.strings.push(value);
          const si = current.sst.createElementNS(MAIN, "si"), t = current.sst.createElementNS(MAIN, "t");
          t.textContent = value; si.append(t); current.sst.documentElement.append(si);
          current.sst.documentElement.setAttribute("uniqueCount", String(current.strings.length)); sharedChanged = true;
        }
        cell.setAttribute("t", "s"); child = current.doc.createElementNS(MAIN, "v"); child.textContent = String(index);
      } else {
        cell.setAttribute("t", "inlineStr"); child = current.doc.createElementNS(MAIN, "is");
        const t = current.doc.createElementNS(MAIN, "t"); t.textContent = value; child.append(t);
      }
      cell.insertBefore(child, cell.firstChild);
    }
    const serializer = new XMLSerializer();
    if (sharedRefDelta && current.sst?.documentElement.hasAttribute("count")) {
      current.sst.documentElement.setAttribute("count", String(Number(current.sst.documentElement.getAttribute("count")) + sharedRefDelta)); sharedChanged = true;
    }
    current.zip.file(current.sheetPath, serializer.serializeToString(current.doc));
    if (sharedChanged) current.zip.file("xl/sharedStrings.xml", serializer.serializeToString(current.sst));
    const output = await current.zip.generateAsync({ type: "uint8array", compression: "DEFLATE", compressionOptions: { level: 6 } });
    const check = await decode(output);
    for (const [key, value] of Object.entries(values)) if (!same(value, check.values[key])) throw Error("写回校验失败：" + key);
    return output;
  }
  function dirty() {
    const values = valuesFor(api.getParameters());
    return book && Object.keys(values).some(key => !same(values[key], book.values[key]));
  }
  function buttons() {
    connectButton.disabled = busy || !window.showOpenFilePicker;
    readButton.disabled = saveButton.disabled = busy || !book;
  }
  function changed() {
    buttons(); if (book && !busy) status.textContent = `${book.name} · ${dirty() ? "有未写回的调整" : "参数与Excel一致"}`;
  }
  async function operation(action) {
    if (busy) return;
    busy = true; buttons();
    try { await action(); }
    catch (error) { if (error.name !== "AbortError") status.textContent = error.message || "参数表操作失败。"; }
    finally { busy = false; buttons(); }
  }
  function remember(fileHandle) {
    return new Promise(resolve => {
      let request;
      // Source workbook now belongs to Charts. Do not reuse a remembered handle to the old art_assets copy.
      try { request = indexedDB.open(options.storage, 1); } catch { resolve(null); return; }
      request.onupgradeneeded = () => request.result.createObjectStore("files");
      request.onerror = () => resolve(null);
      request.onsuccess = () => {
        const db = request.result, tx = db.transaction("files", "readwrite"), store = tx.objectStore("files");
        try { const action = fileHandle ? store.put(fileHandle, "parameters") : store.get("parameters"); action.onsuccess = () => resolve(action.result); action.onerror = () => resolve(null); }
        catch { resolve(null); }
        tx.oncomplete = tx.onerror = () => db.close();
      };
    });
  }
  connectButton.addEventListener("click", () => operation(async () => {
    const [selected] = await window.showOpenFilePicker({ id: options.picker, multiple: false, excludeAcceptAllOption: true,
      types: [{ description: "状态浮层参数表", accept: { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet": [".xlsx"] } }] });
    handle = null; book = null;
    const loaded = await readHandle(selected);
    api.setParameters(loaded.preview); handle = selected; book = loaded;
    await remember(handle); status.textContent = `${book.name} · 已读取，可原位写回`;
  }));
  readButton.addEventListener("click", () => operation(async () => {
    const loaded = await readHandle(handle); api.setParameters(loaded.preview); book = loaded;
    status.textContent = `${book.name} · 已重新读取`;
  }));
  saveButton.addEventListener("click", () => operation(async () => {
    if (!dirty()) { status.textContent = "参数未变化，无需写入。"; return; }
    const latest = await handle.getFile(), bytes = new Uint8Array(await latest.arrayBuffer());
    if (bytes.length !== book.bytes.length || bytes.some((byte, i) => byte !== book.bytes[i])) throw Error("Excel文件已被其他程序修改，请先重新读取后再写回。");
    const values = valuesFor(api.getParameters());
    // Decode a fresh draft: a failed stream must never mutate the connected baseline.
    const output = await encode(await decode(bytes), values);
    let stream;
    try { stream = await handle.createWritable(); await stream.write(output); await stream.close(); }
    catch (error) { if (stream) try { await stream.abort(); } catch {} throw error; }
    book = await readHandle(handle);
    for (const [key, value] of Object.entries(values)) if (!same(value, book.values[key])) throw Error("写入后回读不一致：" + key);
    status.textContent = `${book.name} · 写回成功 ${new Date().toLocaleTimeString()}${dirty() ? "，还有未写回的调整" : ""}`;
  }));
  document.addEventListener(options.event, changed);
  buttons();
  if (!window.showOpenFilePicker) status.textContent = "请用桌面Chrome或Edge打开此页，以连接和原位写回本地Excel。";
  else remember().then(async saved => {
    if (busy || book || !saved) return;
    try {
      if (await saved.queryPermission({ mode: "read" }) !== "granted" || busy || book) return;
      const loaded = await readHandle(saved);
      if (busy || book) return;
      handle = saved; book = loaded; changed(); // Keep locally remembered, possibly unsaved adjustments.
    } catch { /* The explicit connection button remains available. */ }
  });
  }
  window.StatusParameterWorkbook = { mount };
  mount({ fields: FIELDS, api: window.StatusPopupPreview, prefix: "excel", event: "status-preview-change",
    storage: "status-popup-parameter-file-charts-v1", picker: "status-popup-charts" });
})();

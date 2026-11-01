// Unity WebGL版をヘッドレスEdgeで開き、スクリーンショットとコンソールエラーを集める。
// 使い方: node tools/unity-web-check.mjs <出力フォルダ> [URL] [操作 ...]
//   操作: 「x,y」= クリック / 「d:x1,y1>x2,y2」= ドラッグ / 「w:ミリ秒」= 待つ。座標は 400x860 基準。
//   例: node tools/unity-web-check.mjs out http://localhost:8081/ 200,700 w:3000
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync, rmSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

const outDir = process.argv[2] ?? "unity-check-out";
const url = process.argv[3] ?? "http://localhost:8081/";
const ops = process.argv.slice(4);
const edge = "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe";
const port = 9335;
mkdirSync(outDir, { recursive: true });
const profile = join(tmpdir(), "bff-unity-check-" + Date.now());
const browser = spawn(edge, ["--headless=new", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", `--remote-debugging-port=${port}`,
  `--user-data-dir=${profile}`, "--window-size=400,860", "about:blank"], { stdio: "ignore" });
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
let target;
for (let i = 0; i < 50 && !target; i++) {
  try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find(t => t.type === "page"); } catch { await sleep(200); }
}
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => ws.addEventListener("open", r, { once: true }));
let id = 0; const pending = new Map(); const logs = [];
ws.addEventListener("message", (msg) => {
  const d = JSON.parse(msg.data);
  if (d.id && pending.has(d.id)) { pending.get(d.id)(d); pending.delete(d.id); }
  if (d.method === "Runtime.consoleAPICalled") logs.push(`[${d.params.type}] ` + d.params.args.map(a => a.value ?? a.description).join(" "));
  if (d.method === "Runtime.exceptionThrown") logs.push("[exception] " + (d.params.exceptionDetails.exception?.description ?? d.params.exceptionDetails.text));
});
const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });
const shot = async (name) => {
  const r = await send("Page.captureScreenshot", { format: "png" });
  writeFileSync(join(outDir, name + ".png"), Buffer.from(r.result.data, "base64"));
  console.log("screenshot", name);
};
const mouse = (type, x, y) => send("Input.dispatchMouseEvent", { type, x, y, button: "left", buttons: type === "mouseReleased" ? 0 : 1, clickCount: 1 });
const click = async (x, y) => {   // Unityは入力を1フレームごとに読むので、押す→離すの間を空ける
  await send("Input.dispatchMouseEvent", { type: "mouseMoved", x, y }); await sleep(100);
  await mouse("mousePressed", x, y); await sleep(200); await mouse("mouseReleased", x, y);
};
const drag = async (x1, y1, x2, y2) => {
  await send("Input.dispatchMouseEvent", { type: "mouseMoved", x: x1, y: y1 }); await sleep(100);
  await mouse("mousePressed", x1, y1); await sleep(200);
  for (let i = 1; i <= 10; i++) { await mouse("mouseMoved", x1 + (x2 - x1) * i / 10, y1 + (y2 - y1) * i / 10); await sleep(60); }
  await sleep(200); await mouse("mouseReleased", x2, y2);
};
await send("Runtime.enable"); await send("Page.enable");
await send("Emulation.setDeviceMetricsOverride", { width: 400, height: 860, deviceScaleFactor: 1, mobile: false });
await send("Page.navigate", { url });
await sleep(20000);
await shot("01-start");
let n = 2;
for (const op of ops) {
  if (op.startsWith("w:")) { await sleep(Number(op.slice(2))); continue; }
  if (op.startsWith("d:")) {
    const [a, b] = op.slice(2).split(">").map(p => p.split(",").map(Number));
    await drag(a[0], a[1], b[0], b[1]);
  } else { const [x, y] = op.split(",").map(Number); await click(x, y); }
  await sleep(1500);
  await shot(`${String(n++).padStart(2, "0")}-after-${op.replace(/[^0-9a-z,>:]/g, "_")}`);
}
console.log(logs.filter(l => !l.startsWith("[log]") || /error|exception/i.test(l)).slice(0, 40).join("\n") || "no errors");
ws.close(); browser.kill();
try { rmSync(profile, { recursive: true, force: true }); } catch {}

// 生成 Tauri 图标源图：src-tauri/app-icon.png（1024x1024 RGBA）
// 纯 Node 实现（zlib + 手写 PNG 编码），无第三方依赖。
// 设计：圆角方形青绿渐变底 + 白色 ◈（菱形徽记，对齐桌面端视觉令牌 brand #0f766e）。
// 用法：node src-tauri/scripts/make-icon.mjs && npm run tauri icon src-tauri/app-icon.png
import { deflateSync } from 'node:zlib'
import { writeFileSync, mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SIZE = 1024
const SS = 3 // 每轴超采样倍数（3x3 = 9 samples/px）

// ---- PNG 编码 ----
const CRC_TABLE = new Uint32Array(256).map((_, n) => {
  let c = n
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
  return c >>> 0
})
function crc32(buf) {
  let c = 0xffffffff
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}
function chunk(type, data) {
  const len = Buffer.alloc(4)
  len.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body))
  return Buffer.concat([len, body, crc])
}

// ---- 几何（以像素中心为采样点，尺寸单位 = px）----
const cx = SIZE / 2
const cy = SIZE / 2
const R_CORNER = 232 // 圆角半径
const R_OUT = 306 // ◈ 外菱形半对角线
const R_IN = 118 // ◈ 内孔半对角线

// 圆角矩形 SDF（返回 <0 在内部）
function roundedRectDist(x, y) {
  const half = SIZE / 2 - 0.5
  const qx = Math.abs(x - cx) - (half - R_CORNER)
  const qy = Math.abs(y - cy) - (half - R_CORNER)
  const ax = Math.max(qx, 0)
  const ay = Math.max(qy, 0)
  return Math.hypot(ax, ay) + Math.min(Math.max(qx, qy), 0) - R_CORNER
}
// 菱形 L1 距离（到中心曼哈顿距离 - r）
const diamond = (x, y, r) => Math.abs(x - cx) + Math.abs(y - cy) - r

// 渐变色（左上偏亮青绿 → 右下深青绿）
const TOP = [15, 118, 110] // #0f766e brand
const BOTTOM = [9, 78, 72] // 更深的 teal

const raw = Buffer.alloc(SIZE * (SIZE * 4 + 1))
for (let py = 0; py < SIZE; py++) {
  const rowStart = py * (SIZE * 4 + 1)
  raw[rowStart] = 0 // filter: None
  for (let px = 0; px < SIZE; px++) {
    let bgCov = 0
    let fgCov = 0
    let tSum = 0
    for (let sy = 0; sy < SS; sy++) {
      for (let sx = 0; sx < SS; sx++) {
        const x = px + (sx + 0.5) / SS
        const y = py + (sy + 0.5) / SS
        if (roundedRectDist(x, y) <= 0) {
          bgCov++
          // ◈：在外菱形内且不在内孔内
          if (diamond(x, y, R_OUT) <= 0 && diamond(x, y, R_IN) > 0) fgCov++
        }
        tSum += (x + y) / (2 * SIZE)
      }
    }
    const total = SS * SS
    const bgA = bgCov / total
    const fgA = (fgCov / total) // 白色前景（受底色覆盖度截断）
    const t = tSum / total
    const br = TOP[0] + (BOTTOM[0] - TOP[0]) * t
    const bg = TOP[1] + (BOTTOM[1] - TOP[1]) * t
    const bb = TOP[2] + (BOTTOM[2] - TOP[2]) * t
    // 合成：底色 alpha = bgA，前景白 alpha = fgA（已含底覆盖）
    const outA = Math.max(bgA, fgA)
    const o = rowStart + 1 + px * 4
    if (outA <= 0) {
      raw[o] = 0; raw[o + 1] = 0; raw[o + 2] = 0; raw[o + 3] = 0
    } else {
      // 白色前景按 fgA 混合在底色上，再按整体 alpha 预乘到透明画布外为黑（PNG 无预乘，直接输出直通色）
      const mix = (c, w) => Math.round(c + (255 - c) * w)
      raw[o] = mix(br, fgA)
      raw[o + 1] = mix(bg, fgA)
      raw[o + 2] = mix(bb, fgA)
      raw[o + 3] = Math.round(outA * 255)
    }
  }
}

const ihdr = Buffer.alloc(13)
ihdr.writeUInt32BE(SIZE, 0)
ihdr.writeUInt32BE(SIZE, 4)
ihdr[8] = 8 // bit depth
ihdr[9] = 6 // color type RGBA
const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk('IHDR', ihdr),
  chunk('IDAT', deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0)),
])

const out = join(dirname(fileURLToPath(import.meta.url)), '..', 'app-icon.png')
mkdirSync(dirname(out), { recursive: true })
writeFileSync(out, png)
console.log(`written ${out} (${SIZE}x${SIZE}, ${png.length} bytes)`)

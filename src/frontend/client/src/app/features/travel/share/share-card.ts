/**
 * 足迹分享卡（M1 / 达人分享）：纯前端 canvas 合成，无后端依赖。
 * 输出 4:5 竖版卡片 PNG（下载）与可复制的分享文案。
 */
import type { TravelRecord } from '../models/travel-record.model';

export interface ShareCardRow {
  locationName: string;
  arrivedAt: string;
  departedAt: string | null;
  description: string | null;
}

export interface ShareCardInput {
  title: string;
  rows: ShareCardRow[];
}

function pad(n: number): string {
  return String(n).padStart(2, '0');
}

function fmtDate(iso: string): string {
  const d = new Date(iso);
  const y = d.getFullYear();
  const md = `${d.getMonth() + 1}月${d.getDate()}日`;
  return y === new Date().getFullYear() ? md : `${y}年${md}`;
}

function fmtTime(iso: string): string {
  const d = new Date(iso);
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function cardRowRange(row: ShareCardRow): string {
  const start = `${fmtDate(row.arrivedAt)} ${fmtTime(row.arrivedAt)}`;
  return row.departedAt ? `${start} → ${fmtDate(row.departedAt)} ${fmtTime(row.departedAt)}` : `${start} · 进行中`;
}

export function toCardRows(records: readonly TravelRecord[]): ShareCardRow[] {
  return [...records]
    .sort((a, b) => a.arrivedAt.localeCompare(b.arrivedAt))
    .map((r) => ({
      locationName: r.locationName,
      arrivedAt: r.arrivedAt,
      departedAt: r.departedAt,
      description: r.description,
    }));
}

function stripHtml(html: string | null | undefined): string {
  if (!html) return '';
  const text = html.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim();
  const div = document.createElement('div');
  div.innerHTML = text;
  return div.textContent ?? '';
}

/** 复制用文案（小红书/朋友圈友好） */
export function buildShareText(input: ShareCardInput): string {
  const lines = input.rows.slice(0, 12).map((row, i) => {
    const snip = stripHtml(row.description);
    const desc = snip ? ` · ${snip.slice(0, 30)}${snip.length > 30 ? '…' : ''}` : '';
    return `${i + 1}. ${row.locationName}（${cardRowRange(row)}）${desc}`;
  });
  const more = input.rows.length > 12 ? `\n……还有 ${input.rows.length - 12} 站` : '';
  return `🧭 我的旅行足迹「${input.title}」${lines.length} 站\n\n${lines.join('\n')}${more}\n\n—— 用 Travel Map 记录每一次出发 ✈️`;
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number): void {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

function wrap(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string[] {
  const chars = [...text];
  const lines: string[] = [];
  let line = '';
  for (const ch of chars) {
    if (ctx.measureText(line + ch).width > maxWidth && line) {
      lines.push(line);
      line = ch;
    } else {
      line += ch;
    }
  }
  if (line) lines.push(line);
  return lines;
}

/** 渲染 4:5 竖版分享卡（1080×1350） */
export function renderShareCard(input: ShareCardInput): HTMLCanvasElement {
  const W = 1080;
  const H = 1350;
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = H;
  const ctx = canvas.getContext('2d')!;

  // 主题渐变底（暖纸白 → 珊瑚橙），简约鲜明
  const bg = ctx.createLinearGradient(0, 0, 0, H);
  bg.addColorStop(0, '#fff7ee');
  bg.addColorStop(0.55, '#ffead6');
  bg.addColorStop(1, '#ffd9b8');
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, W, H);

  // 装饰圆
  ctx.globalAlpha = 0.5;
  ctx.fillStyle = '#ffb57a';
  ctx.beginPath(); ctx.arc(960, 170, 210, 0, Math.PI * 2); ctx.fill();
  ctx.globalAlpha = 0.35;
  ctx.fillStyle = '#ea580c';
  ctx.beginPath(); ctx.arc(90, 1180, 240, 0, Math.PI * 2); ctx.fill();
  ctx.globalAlpha = 1;

  ctx.textBaseline = 'top';

  // 品牌行
  ctx.fillStyle = '#ea580c';
  ctx.font = '700 44px "PingFang SC","Microsoft YaHei",sans-serif';
  ctx.fillText('🧭 Travel Map 足迹', 72, 84);
  ctx.fillStyle = '#8b7a6a';
  ctx.font = '400 30px "PingFang SC","Microsoft YaHei",sans-serif';
  ctx.fillText('把我的旅行足迹分享给你', 74, 156);

  // 标题
  ctx.fillStyle = '#2a1c10';
  ctx.font = '800 64px "PingFang SC","Microsoft YaHei",sans-serif';
  const titleLines = wrap(ctx, input.title.length > 22 ? input.title.slice(0, 22) + '…' : input.title, W - 180);
  titleLines.forEach((line, i) => ctx.fillText(line, 72, 250 + i * 84));
  const titleBottom = 250 + titleLines.length * 84;

  // 计数徽章
  ctx.fillStyle = '#ea580c';
  roundRect(ctx, 72, titleBottom + 30, 300, 68, 34); ctx.fill();
  ctx.fillStyle = '#ffffff';
  ctx.font = '700 36px "PingFang SC","Microsoft YaHei",sans-serif';
  ctx.fillText(`共 ${input.rows.length} 站足迹`, 96, titleBottom + 46);

  // 记录行
  const maxRows = Math.min(input.rows.length, 8);
  const listTop = titleBottom + 150;
  const rowGap = 118;
  for (let i = 0; i < maxRows; i++) {
    const row = input.rows[i]!;
    const y = listTop + i * rowGap;
    // 序号圆
    ctx.fillStyle = i === 0 ? '#ea580c' : '#ffffff';
    ctx.strokeStyle = '#ea580c';
    ctx.lineWidth = 4;
    roundRect(ctx, 72, y, 74, 74, 24);
    ctx.fill();
    ctx.stroke();
    ctx.fillStyle = i === 0 ? '#ffffff' : '#ea580c';
    ctx.font = '700 40px "PingFang SC","Microsoft YaHei",sans-serif';
    ctx.fillText(String(i + 1), 72 + (74 - ctx.measureText(String(i + 1)).width) / 2, y + 17);

    // 地点
    ctx.fillStyle = '#241a12';
    ctx.font = '700 48px "PingFang SC","Microsoft YaHei",sans-serif';
    ctx.fillText(row.locationName.length > 16 ? row.locationName.slice(0, 16) + '…' : row.locationName, 176, y + 2);

    // 时间
    ctx.fillStyle = '#8b7a6a';
    ctx.font = '400 30px "PingFang SC","Microsoft YaHei",sans-serif';
    ctx.fillText(cardRowRange(row), 176, y + 74);

    // 描述摘录（仅第一行缩进内联）
    const snip = stripHtml(row.description);
    if (snip && i < 3) {
      ctx.fillStyle = '#5a4634';
      ctx.font = '400 28px "PingFang SC","Microsoft YaHei",sans-serif';
      const line = `「${snip.length > 30 ? snip.slice(0, 30) + '…' : snip}」`;
      ctx.fillText(line.length > 34 ? line.slice(0, 34) + '…' : line, 176, y + 40);
    }
  }

  if (input.rows.length > maxRows) {
    ctx.fillStyle = '#8b7a6a';
    ctx.font = '400 32px "PingFang SC","Microsoft YaHei",sans-serif';
    ctx.fillText(`……还有 ${input.rows.length - maxRows} 站旅程，尽在 Travel Map`, 96, H - 96);
  } else {
    ctx.fillStyle = '#8b7a6a';
    ctx.font = '400 32px "PingFang SC","Microsoft YaHei",sans-serif';
    ctx.fillText('用 Travel Map 记录每一次出发 ✈️', 96, H - 96);
  }

  return canvas;
}

export function canvasToPngUrl(canvas: HTMLCanvasElement): string {
  return canvas.toDataURL('image/png');
}

export function downloadPngUrl(url: string, filename: string): void {
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
}

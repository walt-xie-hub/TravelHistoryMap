import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import type { TravelRecord } from '../../models/travel-record.model';
import { TravelIconComponent } from '../travel-icon/travel-icon';
import { iconForRecord, travelIcon } from '../../utils/travel-icon.util';
import {
  buildPostcardTrail,
  pickMainRecord,
  postcardImageBudget,
} from '../../postcard/postcard-model';
import {
  postmarkGeometry,
  postmarkText,
  stampCancelAngle,
  POSTMARK_NOTE,
} from '../../postcard/postcard-postmark';
import { stampCaption, zodiacIconKey } from '../../postcard/postcard-stamp';
import { postcardTemplateById } from '../../postcard/postcard-template';
import type { PostcardContent } from '../../postcard/postcard-model';

/**
 * 明信片画面（ADR-0018）：把内容渲染成 1181×1748 的版式。
 *
 * 这一层是**纯渲染**：不取图片字节、不发请求——图片由父组件取好并以 `imageUrls` 传入，
 * 因此同一份 DOM 既是屏幕上的预览，也是导出（html-to-image）的源。
 * 10 款版式共用邮票 / 邮戳 / 足迹清单 / 收件人栏这几个 `ng-template` 片段，只换排布。
 */
@Component({
  selector: 'app-postcard-canvas',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, TravelIconComponent],
  templateUrl: './postcard-canvas.html',
  styleUrls: ['./postcard-canvas.scss', './postcard-layouts.scss'],
})
export class PostcardCanvas {
  /** 实例序号：SVG 的 mask / textPath 需要页内唯一 id（缩略图与预览会同时挂 10+ 份） */
  private static sequence = 0;

  readonly content = input.required<PostcardContent>();
  readonly records = input.required<readonly TravelRecord[]>();
  /** imageId → 可直接放进 <img> 的地址（blob URL / dataURL） */
  readonly imageUrls = input<ReadonlyMap<number, string>>(new Map());

  /** 本实例的 id 前缀（模板里拼成 mask / textPath 的 id） */
  protected readonly uid = `pc-canvas-${PostcardCanvas.sequence++}`;

  protected readonly template = computed(() => postcardTemplateById(this.content().templateId));
  protected readonly main = computed(() =>
    pickMainRecord(this.records(), this.content().mainRecordId),
  );
  /**
   * 足迹清单只在**多条**记录时出现（ADR-0018 决策 2）：
   * 单条时它既是主角也是全部内容，再列一遍自己毫无意义。这里与编辑器同一口径。
   */
  protected readonly showTrail = computed(
    () => this.content().modules.trail && this.records().length > 1,
  );
  protected readonly trail = computed(() =>
    buildPostcardTrail(
      this.records(),
      this.content().includedRecordIds,
      this.template().trailLimit,
    ),
  );
  protected readonly postmark = computed(() => postmarkGeometry());
  protected readonly cancelAngle = computed(() => stampCancelAngle(this.content().mainRecordId));
  /**
   * 邮戳画布：viewBox 从 (0,0) 起，圆心靠 `translate` 组摆放。
   * 不用 `-size/2 -size/2 size size` 那种“原点居中”的 viewBox：
   * 导出时整份 DOM 会被序列化成 SVG 再当图片渲染，原点偏移在这条路径上不可靠
   * （实测圆/文字会错位到角上），绝对坐标则在任何渲染路径下都一致。
   */
  protected readonly postmarkViewBox = computed(() => `0 0 ${this.postmark().size} ${this.postmark().size}`);
  /** 圆心在画布内的平移量 */
  protected readonly postmarkCenter = computed(() => this.postmark().size / 2);

  /** 选中的图片地址（按选择顺序；缺图时数组更短，版式用占位提示） */
  protected readonly photos = computed(() =>
    this.content()
      .imageIds.map((id) => this.imageUrls().get(id) ?? '')
      .filter((url) => url !== ''),
  );
  protected readonly photoBudget = computed(() =>
    postcardImageBudget(this.template(), this.photos().length),
  );

  /** 生肖剪影：只有图标库里已有对应动物的生肖才画剪影（虎 / 猴） */
  protected readonly stampIcon = computed(() => {
    const key = zodiacIconKey(this.content().stamp.zodiac);
    return key ? travelIcon(key) : undefined;
  });

  /** 该次旅行的旅行标识（图标模块）：显式选择 > 地区特色（ADR-0016），解析为空则不渲染 */
  protected readonly mainIcon = computed(() => {
    const main = this.main();
    return main ? iconForRecord(main) : undefined;
  });

  /** 邮戳的无障碍文案（地名 + 日期 + 纪念字样），视觉由 SVG 呈现 */
  protected readonly postmarkLabel = computed(() =>
    postmarkText(this.content().fields.place, this.content().fields.date),
  );

  protected readonly year = computed(() => this.content().stamp.year);
  protected readonly zodiac = computed(() => this.content().stamp.zodiac);
  /** 票面无障碍文案（与图例同一口径） */
  protected readonly stampLabel = computed(() => stampCaption(this.content().stamp));
  protected readonly postmarkNote = POSTMARK_NOTE;
  /** 邮编格：固定 6 格，未填的格子留白（邮政式样） */
  protected readonly postalDigits = computed(() => {
    const code = this.content().fields.postalCode;
    return Array.from({ length: 6 }, (_, index) => code[index] ?? '');
  });

  /** 坐标小字（坐标模块）：保留 4 位小数，WGS-84 口径与记录一致 */
  protected coordinates(): string {
    const main = this.main();
    if (!main) return '';
    return `${main.latitude.toFixed(4)}, ${main.longitude.toFixed(4)}`;
  }

  protected photoAt(index: number): string {
    return this.photos()[index] ?? '';
  }
}

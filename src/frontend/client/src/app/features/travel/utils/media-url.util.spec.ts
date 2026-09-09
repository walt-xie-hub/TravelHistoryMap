import { createMediaUrlHandle } from './media-url.util';
import type { MediaUrlHandle } from './media-url.util';

describe('media-url.util — MediaUrlHandle', () => {
  let created: string[];
  let revoked: string[];

  beforeEach(() => {
    created = [];
    revoked = [];
    spyOn(URL, 'createObjectURL').and.callFake((blob: any) => {
      const url = `blob:media-${created.length}`;
      created.push(`type=${blob.type};size=${blob.size}`);
      return url;
    });
    spyOn(URL, 'revokeObjectURL').and.callFake((url: string) => {
      revoked.push(url);
    });
  });

  function makeHandle(failures = new Set<string>()): MediaUrlHandle {
    return createMediaUrlHandle({
      fetchBlob: async (path: string) => {
        if (failures.has(path)) throw new Error('fetch failed');
        return new Blob(['x'], { type: 'image/png' });
      },
    });
  }

  it('returns a blob: object URL per media path', async () => {
    const media = makeHandle();
    const url = await media.urlFor('/api/media/a/thumbnail');
    expect(url).toMatch(/^blob:/);
    expect(created.length).toBe(1);
  });

  it('caches and reuses the same URL for the same path (one fetch)', async () => {
    const media = makeHandle();
    const [first, second] = await Promise.all([
      media.urlFor('/api/media/a/original'),
      media.urlFor('/api/media/a/original'),
    ]);
    expect(first).toBe(second); // 并发共享同一次拉取
    expect(created.length).toBe(1);

    const third = await media.urlFor('/api/media/a/original');
    expect(third).toBe(first); // 再次调用仍复用缓存
    expect(created.length).toBe(1);
  });

  it('fetches distinct paths separately', async () => {
    const media = makeHandle();
    await media.urlFor('/api/media/a/thumbnail');
    await media.urlFor('/api/media/b/original');
    expect(created.length).toBe(2);
  });

  it('dispose revokes every created URL and drops the cache', async () => {
    const media = makeHandle();
    await media.urlFor('/api/media/a/thumbnail');
    await media.urlFor('/api/media/b/original');
    expect(created.length).toBe(2);

    media.dispose();
    expect(revoked.length).toBe(2);
  });

  it('does not keep a failed path cached — a later call retries', async () => {
    const media = makeHandle(new Set(['/api/media/bad/original']));
    await expectAsync(media.urlFor('/api/media/bad/original')).toBeRejected();

    // 仍可正常取其他路径
    const ok = await media.urlFor('/api/media/ok/thumbnail');
    expect(ok).toMatch(/^blob:/);
  });
});

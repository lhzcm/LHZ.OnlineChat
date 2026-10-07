/**
 * 图片压缩工具。
 *
 * 为什么需要：聊天图片此前按原文件上传，而气泡里最大只渲染到 260px；
 * 头像裁剪结果导出的是 512×512 **PNG**（300–700KB），每个好友拉取时都要再下一次。
 * 一张 4MB 的手机照片 = 上行 4MB + 每个接收者再下行 4MB。
 */

/** 聊天图片长边上限：够 2 倍图显示，也远小于手机原图 */
const CHAT_MAX_EDGE = 1600

/** 聊天图片质量：0.82 在文字截图与照片之间取平衡 */
const CHAT_QUALITY = 0.82

/** 头像质量：方图内容简单，可以更低 */
const AVATAR_QUALITY = 0.85

function loadImage(url: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error('图片加载失败'))
    img.src = url
  })
}

function canvasToBlob(
  canvas: HTMLCanvasElement,
  type: string,
  quality?: number
): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality))
}

/**
 * 把 canvas 导出成图片 Blob，优先 WebP、不支持时退回 PNG。
 *
 * 不能假定要到的就是 WebP：不支持 WebP 编码的浏览器（Safari 14 之前）
 * 对 toBlob(cb, 'image/webp') 会静默给回 PNG，而服务端是按扩展名校验的，
 * 所以这里显式检查返回类型，让名字与字节始终一致。
 */
export async function canvasToImageBlob(
  canvas: HTMLCanvasElement,
  quality = AVATAR_QUALITY
): Promise<Blob | null> {
  const webp = await canvasToBlob(canvas, 'image/webp', quality)
  if (webp && webp.type === 'image/webp') return webp

  return canvasToBlob(canvas, 'image/png')
}

/** 按实际 MIME 类型给出正确的扩展名 */
export function blobToFile(blob: Blob, baseName: string): File {
  const extension = blob.type === 'image/webp'
    ? 'webp'
    : blob.type === 'image/jpeg'
      ? 'jpg'
      : 'png'
  return new File([blob], `${baseName}.${extension}`, { type: blob.type || 'image/png' })
}

/**
 * 压缩待发送的聊天图片：等比缩到长边 ≤ 1600px 并转 WebP。
 *
 * 以下情况原样返回，让调用方无脑上传即可：
 *   - GIF：canvas 画一遍会丢掉动画帧，宁可不压；
 *   - 压不动（浏览器不支持 WebP 编码、或解码失败）；
 *   - 压完反而更大（本来就是小图 / 已优化过的图，或文字截图的 PNG 比有损 WebP 更小）。
 */
export async function compressChatImage(file: File): Promise<File> {
  if (!file.type.startsWith('image/') || file.type === 'image/gif') return file

  const url = URL.createObjectURL(file)
  try {
    const image = await loadImage(url)
    const scale = Math.min(1, CHAT_MAX_EDGE / Math.max(image.naturalWidth, image.naturalHeight))
    const width = Math.max(1, Math.round(image.naturalWidth * scale))
    const height = Math.max(1, Math.round(image.naturalHeight * scale))

    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height

    const ctx = canvas.getContext('2d')
    if (!ctx) return file
    ctx.drawImage(image, 0, 0, width, height)

    const compressed = await canvasToImageBlob(canvas, CHAT_QUALITY)
    if (!compressed || compressed.size >= file.size) return file

    return blobToFile(compressed, 'image')
  } catch {
    // 压缩失败不该让消息发不出去：退回原文件，由服务端的体积校验兜底
    return file
  } finally {
    URL.revokeObjectURL(url)
  }
}

/**
 * 图片 / 语音消息的地址只认站内上传目录（与后端 MessageContentRules 同口径）。
 *
 * 服务端已拒收外链，这里是第二道防线：库里的历史消息、别的客户端直连 WS 发进来的内容，
 * 都不应该让接收者的浏览器去请求发送者指定的外部地址（泄露 IP、已读状态，或当跟踪像素）。
 * 不合法返回空串，调用方据此渲染占位。
 */
const LOCAL_MEDIA = /^\/uploads\/(images|voices|files)\/[A-Za-z0-9._-]+$/

export function localMediaSrc(url: string | null | undefined): string {
  const value = (url || '').trim()
  return LOCAL_MEDIA.test(value) && !value.includes('..') ? value : ''
}

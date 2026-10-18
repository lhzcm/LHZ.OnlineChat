/**
 * 语音播放的全局单例：同一时刻只让一个语音在播。
 *
 * 与消息菜单同理（见 useMessageMenu）：每条消息各管各的会同时响好几条，
 * 所以播放权收敛到模块级 —— 谁开始播就先把上一个暂停。
 */
let current: HTMLAudioElement | null = null

/** 申请播放权：暂停上一条，返回的 false 表示"自己就是当前在播的"由调用方决定 */
export function claimVoicePlayback(el: HTMLAudioElement) {
  if (current && current !== el) current.pause()
  current = el
}

/** 主动让出（暂停/播完时调用） */
export function releaseVoicePlayback(el: HTMLAudioElement) {
  if (current === el) current = null
}

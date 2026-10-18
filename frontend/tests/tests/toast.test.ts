import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import AppToast from '@/components/AppToast.vue'
import { useToast } from '@/composables/useToast'

/**
 * 轻提示：状态机（自动消失/手动关闭/类型）与浮层渲染。
 *
 * 之前这里是个没有任何样式的 div（贴着页面顶部的裸文本），错误提示几乎看不清；
 * 现在由 AppToast 统一渲染，错误与普通提示用颜色 + 图标区分。
 */
beforeEach(() => {
  useToast().clearToast()
  vi.useRealTimers()
})

describe('useToast 状态', () => {
  it('默认是信息提示，2.6 秒后自动消失', () => {
    vi.useFakeTimers()
    const { toast, toastMsg, toastType } = useToast()

    toast('已复制')

    expect(toastMsg.value).toBe('已复制')
    expect(toastType.value).toBe('info')

    vi.advanceTimersByTime(2600)
    expect(toastMsg.value).toBe('')
  })

  it('错误提示停留更久（4 秒），类型可区分', () => {
    vi.useFakeTimers()
    const { toast, toastMsg, toastType } = useToast()

    toast('没有检测到麦克风', 'error')

    expect(toastType.value).toBe('error')
    vi.advanceTimersByTime(2600)
    expect(toastMsg.value).not.toBe('')      // 2.6s 时还在
    vi.advanceTimersByTime(1400)
    expect(toastMsg.value).toBe('')
  })

  it('连续提示会重置计时，不会提前消失', () => {
    vi.useFakeTimers()
    const { toast, toastMsg } = useToast()

    toast('第一条')
    vi.advanceTimersByTime(2000)
    toast('第二条')
    vi.advanceTimersByTime(2000)

    expect(toastMsg.value).toBe('第二条')

    vi.advanceTimersByTime(600)
    expect(toastMsg.value).toBe('')
  })

  it('clearToast 立刻关闭', () => {
    const { toast, toastMsg, clearToast } = useToast()

    toast('提示')

    clearToast()

    expect(toastMsg.value).toBe('')
  })
})

describe('AppToast 渲染', () => {
  it('没有消息时不渲染', () => {
    const wrapper = mount(AppToast)

    expect(wrapper.find('.app-toast').exists()).toBe(false)
  })

  it('信息提示：渲染文案与 info 样式', () => {
    useToast().toast('已复制')

    const wrapper = mount(AppToast)

    expect(wrapper.find('.app-toast').text()).toContain('已复制')
    expect(wrapper.find('.app-toast').classes()).toContain('info')
    expect(wrapper.find('.app-toast').attributes('role')).toBe('status')
  })

  it('错误提示：带 error 样式与警告图标', () => {
    useToast().toast('没有检测到麦克风', 'error')

    const wrapper = mount(AppToast)

    const toast = wrapper.find('.app-toast')
    expect(toast.classes()).toContain('error')
    expect(toast.text()).toContain('没有检测到麦克风')
    expect(wrapper.find('.toast-icon').text()).toBe('⚠')
  })

  it('点击卡片立即关闭', async () => {
    useToast().toast('提示')

    const wrapper = mount(AppToast)
    await wrapper.find('.app-toast').trigger('click')

    expect(useToast().toastMsg.value).toBe('')
    expect(wrapper.find('.app-toast').exists()).toBe(false)
  })
})

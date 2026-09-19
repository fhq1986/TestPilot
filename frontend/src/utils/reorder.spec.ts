import { describe, it, expect } from 'vitest'
import { isAfterRow, moveInList } from './reorder'

/**
 * 拖拽重排的下标运算。
 *
 * 这段算术错了之后表现很隐蔽——拖拽看起来能拖，只是落点差一位，
 * 用户会以为是手感问题而不是 bug。所以这里把每个方向、每个边界都钉死。
 */
describe('moveInList', () => {
  const list = ['A', 'B', 'C', 'D']

  it('向下拖：插到目标行之前', () => {
    // A 拖到 C 的上半部分 → [B, A, C, D]
    expect(moveInList(list, 0, 2, false)).toEqual(['B', 'A', 'C', 'D'])
  })

  it('向下拖：插到目标行之后', () => {
    // A 拖到 C 的下半部分 → [B, C, A, D]
    expect(moveInList(list, 0, 2, true)).toEqual(['B', 'C', 'A', 'D'])
  })

  it('向上拖：插到目标行之前', () => {
    // D 拖到 B 的上半部分 → [A, D, B, C]
    expect(moveInList(list, 3, 1, false)).toEqual(['A', 'D', 'B', 'C'])
  })

  it('向上拖：插到目标行之后', () => {
    // D 拖到 B 的下半部分 → [A, B, D, C]
    expect(moveInList(list, 3, 1, true)).toEqual(['A', 'B', 'D', 'C'])
  })

  it('能把元素拖到列表最末', () => {
    // 这是只看行号会失败的那个场景：拖到最后一行的下半部分
    expect(moveInList(list, 0, 3, true)).toEqual(['B', 'C', 'D', 'A'])
  })

  it('能把元素拖到列表最前', () => {
    expect(moveInList(list, 3, 0, false)).toEqual(['D', 'A', 'B', 'C'])
  })

  it('先摘除再插入的位移被正确处理', () => {
    // 若忘了「插入点在源之后要减一」，这条会得到 [B, C, A, D] 而不是 [B, A, C, D]
    expect(moveInList(list, 1, 3, false)).toEqual(['A', 'C', 'B', 'D'])
  })

  it('拖回原位返回 null（不产生无意义的保存）', () => {
    // 插到自己前面
    expect(moveInList(list, 2, 2, false)).toBeNull()
    // 插到自己后面（等价于没动）
    expect(moveInList(list, 2, 2, true)).toBeNull()
  })

  it('向后拖一位也视为未位移', () => {
    // B 拖到 C 的上半部分 → 结果仍是 [A, B, C, D]
    expect(moveInList(list, 1, 2, false)).toBeNull()
  })

  it('元素少于两个时不做任何事', () => {
    expect(moveInList([], 0, 0, false)).toBeNull()
    expect(moveInList(['A'], 0, 0, true)).toBeNull()
  })

  it('越界下标返回 null 而不是抛异常', () => {
    expect(moveInList(list, -1, 2, false)).toBeNull()
    expect(moveInList(list, 9, 2, false)).toBeNull()
    expect(moveInList(list, 0, -1, false)).toBeNull()
    expect(moveInList(list, 0, 9, false)).toBeNull()
  })

  it('不修改入参', () => {
    const original = [...list]
    moveInList(list, 0, 2, false)
    expect(list).toEqual(original)
  })

  it('任意一次拖拽后元素集合不变（只是顺序变化）', () => {
    for (let from = 0; from < list.length; from++) {
      for (let over = 0; over < list.length; over++) {
        for (const after of [false, true]) {
          const result = moveInList(list, from, over, after)
          if (result === null) continue
          expect([...result].sort()).toEqual([...list].sort())
          expect(result).toHaveLength(list.length)
        }
      }
    }
  })
})

describe('isAfterRow', () => {
  it('指针在行中线以下是「之后」，中线本身算「之前」', () => {
    expect(isAfterRow(121, 100, 40)).toBe(true)
    expect(isAfterRow(119, 100, 40)).toBe(false)
    expect(isAfterRow(100, 100, 40)).toBe(false)
    // 恰好压在中线上取「之前」：用严格大于，与 sortablejs 等通行实现一致
    expect(isAfterRow(120, 100, 40)).toBe(false)
  })
})

/**
 * 列表拖拽重排的下标运算。
 *
 * 抽成纯函数是因为这里的算术最容易出错，而且错了之后表现很隐蔽——
 * 拖拽看起来「能拖」，只是落点差一位，用户会以为是自己没拖准。
 * 几个必须处理对的地方：
 *
 * 1. **先摘除再插入会位移**：把元素从 from 摘下后，位于 from 之后的插入点都要减 1，
 *    否则向下拖动时结果会比预期低一位。
 * 2. **插入点是「目标行之前 / 之后」而不是目标行本身**：拖到某行上半部分插到它前面、
 *    下半部分插到它后面。只看行号的话，永远没法把元素拖到列表最末。
 * 3. **没有实际位移要能被识别**：拖回原位不该产生一次无意义的保存请求。
 */
export function moveInList<T>(
  list: readonly T[],
  from: number,
  overIndex: number,
  after: boolean,
): T[] | null {
  if (list.length < 2) return null
  if (from < 0 || from >= list.length) return null
  if (overIndex < 0 || overIndex >= list.length) return null

  // 「插到 overIndex 之前」= 下标 overIndex；「之后」= 下标 overIndex + 1
  let insertAt = after ? overIndex + 1 : overIndex

  // 拖回原位（插到自己前面，或紧跟自己后面）都等于没动
  if (insertAt === from || insertAt === from + 1) return null

  const next = [...list]
  const [moved] = next.splice(from, 1)
  // 摘除后，原本在 from 之后的插入点整体前移一位
  if (insertAt > from) insertAt -= 1
  next.splice(insertAt, 0, moved)
  return next
}

/**
 * 由指针纵坐标判定插入点在目标行的上半还是下半。
 * 分界线取行中线——这是列表拖拽的通行约定，用户不需要学习。
 * 恰好压在中线上取「之前」（严格大于），与 sortablejs 等实现一致。
 */
export function isAfterRow(pointerY: number, rowTop: number, rowHeight: number): boolean {
  return pointerY > rowTop + rowHeight / 2
}

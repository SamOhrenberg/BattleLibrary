import type { ReactNode } from 'react'

// Renders BSData rules text, which marks up spans with **double asterisks** for
// bold and ^^double carets^^ for keywords (the two nest in either order, e.g.
// **^^Terminator Captain^^**). Markers toggle styling; nodes are built rather
// than injecting HTML, so the text stays safe.
export function RuleText({ text }: { text: string }) {
  const nodes: ReactNode[] = []
  let buffer = ''
  let bold = false
  let keyword = false
  let key = 0

  const flush = () => {
    if (!buffer) return
    const content = buffer
    buffer = ''
    if (bold && keyword) nodes.push(<strong className="kw-text" key={key++}>{content}</strong>)
    else if (bold) nodes.push(<strong key={key++}>{content}</strong>)
    else if (keyword) nodes.push(<span className="kw-text" key={key++}>{content}</span>)
    else nodes.push(<span key={key++}>{content}</span>)
  }

  for (let i = 0; i < text.length; i++) {
    const pair = text[i] + text[i + 1]
    if (pair === '**') {
      flush()
      bold = !bold
      i++
    } else if (pair === '^^') {
      flush()
      keyword = !keyword
      i++
    } else {
      buffer += text[i]
    }
  }
  flush()

  return <>{nodes}</>
}

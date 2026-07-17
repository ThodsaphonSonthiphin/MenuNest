import {useEffect, useRef} from 'react'

type Kind = 'good' | 'bad' | 'none'
const SCENE: Record<Kind, {waterFrac: number; rain: number; lightning: boolean; sky: [string, string]}> = {
  bad: {waterFrac: 0.46, rain: 5, lightning: true, sky: ['#4c5a68', '#8a97a1']},
  good: {waterFrac: 0.26, rain: 0, lightning: false, sky: ['#8fc7e8', '#d7eefb']},
  none: {waterFrac: 0.32, rain: 1, lightning: false, sky: ['#9aa2a9', '#d6dade']},
}

export function WeatherDiorama({kind}: {kind: Kind}) {
  const ref = useRef<HTMLCanvasElement>(null)
  const kindRef = useRef<Kind>(kind)
  kindRef.current = kind

  useEffect(() => {
    const cv = ref.current
    if (!cv) return
    const ctx = cv.getContext('2d')
    if (!ctx) return
    const W = (cv.width = cv.clientWidth * devicePixelRatio)
    const H = (cv.height = cv.clientHeight * devicePixelRatio)
    ctx.scale(1, 1)
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches

    type Drop = {x: number; y: number; v: number}
    const drops: Drop[] = []
    let ripples: {x: number; y: number; r: number}[] = []
    let bolt: {x: number; segs: number[]} | null = null
    let flash = 0
    let raf = 0
    let running = false

    const rand = (n: number) => (Math.sin((n + drops.length) * 12.9898) * 43758.5453 % 1 + 1) % 1 // deterministic-ish

    const frame = (t: number) => {
      const s = SCENE[kindRef.current]
      const waterY = H * (1 - s.waterFrac)
      // sky
      const g = ctx.createLinearGradient(0, 0, 0, H)
      g.addColorStop(0, s.sky[0]); g.addColorStop(1, s.sky[1])
      ctx.fillStyle = g; ctx.fillRect(0, 0, W, H)
      // rocks (สามพันโบก hump), dark
      ctx.fillStyle = '#3b342d'
      ctx.beginPath(); ctx.moveTo(0, H)
      ctx.quadraticCurveTo(W * 0.3, H * 0.45, W * 0.55, H * 0.62)
      ctx.quadraticCurveTo(W * 0.8, H * 0.8, W, H * 0.6)
      ctx.lineTo(W, H); ctx.closePath(); ctx.fill()
      // sun (good)
      if (s.rain === 0) { ctx.fillStyle = 'rgba(255,241,196,0.9)'; ctx.beginPath(); ctx.arc(W * 0.8, H * 0.28, H * 0.14, 0, Math.PI * 2); ctx.fill() }
      // water (semi-transparent → rocks read submerged in bad)
      ctx.fillStyle = kindRef.current === 'bad' ? 'rgba(60,90,110,0.82)' : 'rgba(120,170,200,0.6)'
      ctx.fillRect(0, waterY, W, H - waterY)
      // rain
      if (s.rain > 0 && !reduced) {
        while (drops.length < s.rain * 12) drops.push({x: rand(drops.length) * W, y: rand(drops.length + 1) * H, v: 6 + rand(drops.length + 2) * 8})
        ctx.strokeStyle = 'rgba(220,230,240,0.5)'; ctx.lineWidth = 1
        for (const d of drops) {
          ctx.beginPath(); ctx.moveTo(d.x, d.y); ctx.lineTo(d.x - 2, d.y + 9); ctx.stroke()
          d.y += d.v; if (d.y > waterY) { d.y = -10; d.x = rand(d.x) * W; if (ripples.length < 20) ripples.push({x: d.x, y: waterY, r: 0}) }
        }
      }
      // ripples
      ctx.strokeStyle = 'rgba(255,255,255,0.35)'
      ripples = ripples.filter((r) => r.r < 18)
      for (const r of ripples) { ctx.beginPath(); ctx.ellipse(r.x, r.y, r.r, r.r * 0.3, 0, 0, Math.PI * 2); ctx.stroke(); r.r += 0.6 }
      // lightning (bad)
      if (s.lightning && !reduced) {
        if (!bolt && Math.floor(t / 900) % 3 === 0) bolt = {x: W * (0.3 + rand(t) * 0.4), segs: [0, 0.2, -0.15, 0.25, 0]}
        if (bolt) {
          flash = Math.min(1, flash + 0.3)
          ctx.strokeStyle = 'rgba(255,255,255,0.95)'; ctx.lineWidth = 2; ctx.beginPath()
          let y = 0, x = bolt.x
          ctx.moveTo(x, y)
          for (const dx of bolt.segs) { x += dx * 40; y += H * 0.16; ctx.lineTo(x, y) }
          ctx.stroke()
          if (Math.floor(t / 900) % 3 !== 0) bolt = null
        } else flash = Math.max(0, flash - 0.08)
        if (flash > 0) { ctx.fillStyle = `rgba(255,255,255,${flash * 0.25})`; ctx.fillRect(0, 0, W, H) }
      }
      if (running) raf = requestAnimationFrame(frame)
    }

    const io = new IntersectionObserver(
      ([e]) => {
        if (e.isIntersecting && !reduced) { if (!running) { running = true; raf = requestAnimationFrame(frame) } }
        else { running = false; cancelAnimationFrame(raf); if (reduced) frame(0) }
      },
      {threshold: 0.05},
    )
    io.observe(cv)
    if (reduced) frame(0)
    return () => { running = false; cancelAnimationFrame(raf); io.disconnect() }
  }, [])

  return <canvas ref={ref} className="weather-diorama" aria-hidden="true" />
}

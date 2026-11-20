import { useEffect, useRef } from "react";

/**
 * Kurzer Konfetti-Regen, wenn "Mein Tag" komplett erledigt ist.
 * Reines Canvas, keine Bibliothek, respektiert "Bewegung reduzieren".
 */
export function Confetti({ fire }: { fire: number }) {
  const ref = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    if (!fire || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const canvas = ref.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = window.innerWidth * dpr;
    canvas.height = window.innerHeight * dpr;
    ctx.scale(dpr, dpr);

    const colors = ["#5b5fc7", "#7f85f5", "#13a10e", "#f7630c", "#ffb900", "#e3008c"];
    const pieces = Array.from({ length: 140 }, () => ({
      x: window.innerWidth / 2 + (Math.random() - 0.5) * 240,
      y: window.innerHeight * 0.35,
      vx: (Math.random() - 0.5) * 14,
      vy: -Math.random() * 14 - 4,
      size: 5 + Math.random() * 6,
      rot: Math.random() * Math.PI,
      vr: (Math.random() - 0.5) * 0.3,
      color: colors[Math.floor(Math.random() * colors.length)],
    }));

    let frame = 0;
    let raf = 0;
    const tick = () => {
      frame++;
      ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
      for (const p of pieces) {
        p.vy += 0.35;
        p.vx *= 0.99;
        p.x += p.vx;
        p.y += p.vy;
        p.rot += p.vr;
        ctx.save();
        ctx.translate(p.x, p.y);
        ctx.rotate(p.rot);
        ctx.globalAlpha = Math.max(0, 1 - frame / 140);
        ctx.fillStyle = p.color;
        ctx.fillRect(-p.size / 2, -p.size / 4, p.size, p.size / 2);
        ctx.restore();
      }
      if (frame < 140) raf = requestAnimationFrame(tick);
      else ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [fire]);

  return (
    <canvas
      ref={ref}
      aria-hidden="true"
      style={{ position: "fixed", inset: 0, width: "100vw", height: "100vh", pointerEvents: "none", zIndex: 1000 }}
    />
  );
}

"""Render the bloom intro videos from bloom3d.py (Spike S3).

Outputs (in ./output):
  intro-full.mp4       0.3 s - 8.6 s, H.264 CRF 18, 60 fps, 2x resolution
  intro-short.mp4      4.5 s - 8.6 s, same settings
  intro-full-hevc10.mp4  optional 10-bit HEVC variant (only if the ffmpeg build supports it)
  intro-still.png      frame at 6.8 s (reduced-motion fallback)
  compare-*.png        original 1x frame next to the downscaled 2x frame, for the visual check
  render-info.txt      sizes, timings, settings, calibrated glow gain

Requirements: Python 3.12, numpy, pygame, imageio-ffmpeg (see requirements.txt).
On Windows the font is Consolas, exactly like the original.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
OUT = HERE / "output"
SCALE = float(os.environ.get("BLOOM_INTRO_SCALE", "2"))
FPS = int(os.environ.get("BLOOM_INTRO_FPS", "60"))
COMPARE_TIMES = (2.5, 5.0, 6.8, 8.0)
STILL_TIME = 6.8
FULL = (0.3, 8.6)
SHORT = (4.5, 8.6)


ENV = dict(os.environ, SDL_VIDEODRIVER="dummy", SDL_AUDIODRIVER="dummy", PYGAME_HIDE_SUPPORT_PROMPT="1")


def render_still(scale: float, t: float, folder: Path, glow_gain: float = 1.0) -> Path:
    cmd = [sys.executable, str(HERE / "bloom3d.py"), "--at", str(t), "--scale", str(scale),
           "--glow-gain", f"{glow_gain:.4f}", "--out", str(folder)]
    subprocess.run(cmd, check=True, env=ENV, cwd=HERE, stdout=subprocess.DEVNULL)
    return folder / "still.png"


def mean_brightness(png: Path, size: tuple[int, int] = (960, 760)) -> float:
    import numpy as np
    import pygame

    img = pygame.transform.smoothscale(pygame.image.load(str(png)), size)
    return float(pygame.surfarray.array3d(img).astype(np.float64).mean())


def calibrate_glow(scale: float) -> float:
    """Find the glow gain at which the scaled render is as bright as the original (t = 6.8 s)."""
    if scale == 1.0:
        return 1.0
    tmp = OUT / "calibration"
    target = mean_brightness(render_still(1.0, STILL_TIME, tmp / "orig"))
    g0, g1 = 1.0, 1.4
    b0 = mean_brightness(render_still(scale, STILL_TIME, tmp / "g0", g0))
    b1 = mean_brightness(render_still(scale, STILL_TIME, tmp / "g1", g1))
    for i in range(6):
        if abs(b1 - target) / target < 0.005 or b1 == b0:
            break
        g0, b0, g1 = g1, b1, g1 + (target - b1) * (g1 - g0) / (b1 - b0)
        g1 = min(max(g1, 0.5), 3.0)
        b1 = mean_brightness(render_still(scale, STILL_TIME, tmp / f"s{i}", g1))
    shutil.rmtree(tmp, ignore_errors=True)
    return g1


def render_frames(scale: float, fps: int, folder: Path, max_frames: int = 0, glow_gain: float = 1.0) -> float:
    if folder.exists():
        shutil.rmtree(folder)
    cmd = [sys.executable, str(HERE / "bloom3d.py"), "--record", "--scale", str(scale),
           "--fps", str(fps), "--out", str(folder), "--glow-gain", f"{glow_gain:.4f}"]
    if max_frames:
        cmd += ["--max-frames", str(max_frames)]
    start = time.perf_counter()
    subprocess.run(cmd, check=True, env=ENV, cwd=HERE, stdout=subprocess.DEVNULL)
    return time.perf_counter() - start


def encode(frames: Path, fps: int, span: tuple[float, float], target: Path, codec: str = "h264") -> bool:
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    start_frame = int(round(span[0] * fps))
    count = int(round((span[1] - span[0]) * fps))
    common = [ffmpeg, "-y", "-loglevel", "error", "-framerate", str(fps), "-start_number", str(start_frame),
              "-i", str(frames / "f_%04d.png"), "-frames:v", str(count)]
    if codec == "h264":
        args = ["-c:v", "libx264", "-preset", "slow", "-crf", "18", "-tune", "animation",
                "-pix_fmt", "yuv420p", "-movflags", "+faststart"]
    else:
        args = ["-c:v", "libx265", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p10le",
                "-tag:v", "hvc1", "-x265-params", "log-level=error", "-movflags", "+faststart"]
    result = subprocess.run(common + args + [str(target)])
    return result.returncode == 0


def frame_path(folder: Path, fps: int, t: float) -> Path:
    return folder / f"f_{int(round(t * fps)):04d}.png"


def compare(orig: Path, hires: Path, t: float) -> Path:
    import pygame

    a = pygame.image.load(str(frame_path(orig, 30, t)))
    b = pygame.image.load(str(frame_path(hires, FPS, t)))
    b_small = pygame.transform.smoothscale(b, a.get_size())
    w, h = a.get_size()
    canvas = pygame.Surface((w * 2 + 16, h))
    canvas.fill((40, 40, 40))
    canvas.blit(a, (0, 0))
    canvas.blit(b_small, (w + 16, 0))
    target = OUT / f"compare-{t:.1f}s.png"
    pygame.image.save(canvas, str(target))
    return target


def main() -> None:
    OUT.mkdir(exist_ok=True)
    info: list[str] = [f"scale={SCALE} fps={FPS}"]

    gain = calibrate_glow(SCALE)
    info.append(f"glow gain (calibrated against 1x at {STILL_TIME} s): {gain:.3f}")

    hires = HERE / "frames-hires"
    seconds = render_frames(SCALE, FPS, hires, glow_gain=gain)
    info.append(f"hires render: {seconds:.0f} s")

    orig = HERE / "frames-orig"
    needed = int(max(COMPARE_TIMES) * 30) + 2
    seconds = render_frames(1.0, 30, orig, max_frames=needed)
    info.append(f"original render ({needed} frames): {seconds:.0f} s")

    for name, span in (("intro-full.mp4", FULL), ("intro-short.mp4", SHORT)):
        if not encode(hires, FPS, span, OUT / name):
            raise SystemExit(f"encoding {name} failed")
        info.append(f"{name}: {(OUT / name).stat().st_size / 1e6:.2f} MB")

    if encode(hires, FPS, FULL, OUT / "intro-full-hevc10.mp4", codec="hevc"):
        info.append(f"intro-full-hevc10.mp4: {(OUT / 'intro-full-hevc10.mp4').stat().st_size / 1e6:.2f} MB")
    else:
        info.append("intro-full-hevc10.mp4: not available (ffmpeg build without libx265)")

    shutil.copy(frame_path(hires, FPS, STILL_TIME), OUT / "intro-still.png")
    for t in COMPARE_TIMES:
        compare(orig, hires, t)

    (OUT / "render-info.txt").write_text("\n".join(info) + "\n", encoding="utf-8")
    print("\n".join(info))


if __name__ == "__main__":
    main()

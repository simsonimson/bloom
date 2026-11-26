# Based on bloom3d.py by Edward Jones
# https://github.com/edwardjones7/ascii-art (MIT License, see LICENSE in this folder)
#
# Modified for bloom (tools/intro-render):
#   --scale S        render at S x 960x760 (font, projection and centre scaled with it)
#   --fps N          frames per second for --record (original: 30)
#   --out DIR        output folder for --record frames
#   --max-frames N   stop after N frames (for quick tests)
#   --glow-gain G    multiply the glow passes by G (thinner glyph strokes at higher
#                    scales produce less glow; render.py calibrates G against 1x)
#   --at T           render only the frame at T seconds to --out/still.png
# The random grid used for the "settle" glitch is drawn at the original 1x grid size
# and upsampled, so petal geometry stays identical to the original at every scale.
# The glow passes downsample to the original 1x sizes, so the glow radius is identical too.
import math
import os
import sys
import time

import argparse

import numpy as np
import pygame

_ap = argparse.ArgumentParser()
_ap.add_argument("--record", action="store_true")
_ap.add_argument("--scale", type=float, default=1.0)
_ap.add_argument("--fps", type=int, default=30)
_ap.add_argument("--out", default="frames3d")
_ap.add_argument("--max-frames", type=int, default=0)
_ap.add_argument("--glow-gain", type=float, default=1.0)
_ap.add_argument("--at", type=float, default=None, help="render only this time (s) to --out/still.png")
ARGS = _ap.parse_args()
S = ARGS.scale

W, H = int(round(960 * S)), int(round(760 * S))
FONT_SIZE = int(round(10 * S))
FPS_RECORD = ARGS.fps
DURATION = 10.0
MESSAGE = "let it bloom "
TYPED = "bloom()"
RAMP = ".:;=+*xo%#&@"
GLITCH = "01<>{}[]#$%&*+=?!ABXZ"
BG = (0, 0, 0)
ELEV = math.radians(55)
SCALE = 265 * S
CY = H * 0.56
LIGHT = np.array([-0.45, -0.75, 0.9])

LAYERS = [
    (6, 1.00, 0.34, 80, 2.0, 5.2, 0.70, 0.00, 1.00),
    (6, 0.80, 0.30, 58, 2.3, 5.5, 0.50, math.pi / 6, 0.95),
    (5, 0.62, 0.27, 40, 2.6, 5.8, 0.35, 0.30, 0.90),
    (4, 0.46, 0.24, 26, 2.9, 6.1, 0.20, 0.90, 0.85),
]
BLOW_START = 7.6

RECORD = ARGS.record
OUT_DIR = ARGS.out
if RECORD:
    os.makedirs(OUT_DIR, exist_ok=True)

pygame.init()
screen = pygame.display.set_mode((W, H))
pygame.display.set_caption("bloom3d.py")
FONT_NAMES = "consolas,menlo,dejavusansmono,liberationmono,couriernew"
font = pygame.font.SysFont(FONT_NAMES, FONT_SIZE, bold=True)
cw, ch = font.size("M")
COLS, ROWS = W // cw, H // ch
_font1 = pygame.font.SysFont(FONT_NAMES, 10, bold=True)
_cw1, _ch1 = _font1.size("M")
COLS1, ROWS1 = 960 // _cw1, 760 // _ch1

RAMP_ARR = np.array(list(RAMP))
LETTERS = np.array([c for c in MESSAGE if c != " "])
MSG_GRID = np.array([[MESSAGE[(r * COLS + c) % len(MESSAGE)] for c in range(COLS)]
                     for r in range(ROWS)])
rng = np.random.default_rng(11)
_settle1 = 0.12 + rng.random((ROWS1, COLS1)) * 0.5
SETTLE = _settle1[(np.arange(ROWS) * ROWS1 // ROWS)[:, None], (np.arange(COLS) * COLS1 // COLS)[None, :]]

def ss(a, b, x):
    k = np.clip((x - a) / (b - a), 0.0, 1.0)
    return k * k * (3 - 2 * k)

U, V = 64, 45
uu = np.linspace(0, 1, U)[None, :, None]
vv = np.linspace(-1, 1, V)[None, None, :]

pet = {k: [] for k in ("theta", "L", "W", "open", "t0", "t1", "curl", "tint", "delay")}
for li, (n, L, Wd, op, t0, t1, curl, az, tint) in enumerate(LAYERS):
    for k in range(n):
        pet["theta"].append(az + 2 * math.pi * k / n + rng.uniform(-0.08, 0.08))
        pet["L"].append(L * rng.uniform(0.95, 1.05))
        pet["W"].append(Wd)
        pet["open"].append(math.radians(op))
        pet["t0"].append(t0 + rng.uniform(-0.1, 0.1))
        pet["t1"].append(t1)
        pet["curl"].append(curl)
        pet["tint"].append(tint * rng.uniform(0.8, 1.1))
        pet["delay"].append(BLOW_START + 0.22 * li + rng.uniform(0, 0.35))
pet = {k: np.array(v) for k, v in pet.items()}
P = len(pet["theta"])
NPET = P * U * V

JIT = rng.normal(size=(NPET, 3)) * np.array([0.6, 0.6, 0.9])
PID = rng.integers(0, 10_000, NPET)

STOPS = np.array([[0.70, 0.02, 0.20], [1.00, 0.12, 0.38], [1.00, 0.42, 0.62], [1.00, 0.80, 0.84]])
cu = np.linspace(0, 1, U) * (len(STOPS) - 1)
i0 = np.minimum(cu.astype(int), len(STOPS) - 2)
fr = (cu - i0)[:, None]
UCOL = STOPS[i0] * (1 - fr) + STOPS[i0 + 1] * fr
PCOL = (UCOL[None, :, None, :] * pet["tint"][:, None, None, None]
        * np.ones((1, 1, V, 1))).reshape(-1, 3)

AO = ((0.25 + 0.75 * np.linspace(0, 1, U) ** 0.8)[None, :, None]
      * (1 - 0.35 * np.abs(np.linspace(-1, 1, V)) ** 3)[None, None, :]
      * np.ones((P, 1, 1))).reshape(-1)

NC = 2600
kk = np.arange(NC)
C_R = np.sqrt((kk + 0.5) / NC)
C_A = kk * 2.39996323
C_JIT = rng.normal(size=(NC, 3)) * np.array([0.5, 0.5, 0.9])
C_PID = rng.integers(0, 10_000, NC)
C_COL = np.array([1.0, 0.85, 0.35]) * (1 - C_R[:, None]) + np.array([1.0, 0.45, 0.08]) * C_R[:, None]
C_DELAY = BLOW_START + 1.15

def petals(t):
    prog = ss(pet["t0"], pet["t1"], t)
    alpha = np.radians(6) + (pet["open"] - np.radians(6)) * prog
    phi = alpha[:, None, None] * (0.25 + 0.75 * uu) + (pet["curl"] * prog)[:, None, None] * uu ** 2
    du = 1 / (U - 1)
    Lr = pet["L"][:, None, None]
    rho = np.cumsum(np.sin(phi), axis=1) * du * Lr
    z = np.cumsum(np.cos(phi), axis=1) * du * Lr
    w = pet["W"][:, None, None] * np.sin(np.pi * uu ** 1.6) ** 0.7
    off_t = vv * w
    off_n = 0.55 * vv ** 2 * w
    R = 0.04 + rho - off_n * np.cos(phi)
    Z = z + off_n * np.sin(phi)
    th = pet["theta"][:, None, None]
    x = R * np.cos(th) - off_t * np.sin(th)
    y = R * np.sin(th) + off_t * np.cos(th)
    pts = np.stack(np.broadcast_arrays(x, y, Z), axis=-1)
    n = np.cross(np.gradient(pts, axis=1), np.gradient(pts, axis=2))
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-9
    delay = np.repeat(pet["delay"], U * V)
    return pts.reshape(-1, 3), n.reshape(-1, 3), delay

def centre(t):
    rc = 0.17 * ss(3.0, 5.5, t)
    r = C_R * rc
    pts = np.stack([r * np.cos(C_A), r * np.sin(C_A), 0.10 + 0.10 * (1 - C_R ** 2) * ss(3, 5.5, t)], -1)
    n = np.stack([np.cos(C_A) * C_R * 0.7, np.sin(C_A) * C_R * 0.7, np.ones(NC)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return pts, n

CAM_D = np.array([0.0, -math.cos(ELEV), math.sin(ELEV)])
CAM_UP = np.array([0.0, math.sin(ELEV), math.cos(ELEV)])
LIGHT_N = LIGHT / np.linalg.norm(LIGHT)
HALF = (LIGHT_N + CAM_D) / np.linalg.norm(LIGHT_N + CAM_D)
WIND = np.array([1.7, 0.0, 0.75])

def rasterize(t):
    pp, pn, pdelay = petals(t)
    cp, cn = centre(t)
    pos = np.concatenate([pp, cp])
    nrm = np.concatenate([pn, cn])
    col = np.concatenate([PCOL, C_COL])
    delay = np.concatenate([pdelay, np.full(NC, C_DELAY)])
    jit = np.concatenate([JIT, C_JIT])
    ao = np.concatenate([AO, np.ones(NC)])
    pid = np.concatenate([PID, C_PID])

    s = ss(0.9, 2.6, t) * (1 + 0.02 * math.sin(2 * math.pi * (t - 6) / 2.4) * ss(6, 6.8, t))
    if s <= 0:
        return None
    pos = pos * s

    yaw = 0.4 + 0.32 * t
    cy_, sy_ = math.cos(yaw), math.sin(yaw)
    rot = np.array([[cy_, -sy_, 0], [sy_, cy_, 0], [0, 0, 1]])
    pos = pos @ rot.T
    nrm = nrm @ rot.T

    tau = np.maximum(0.0, t - delay)

    brk = np.maximum(0.0, tau - 0.3)
    pos = pos + WIND * (tau ** 2)[:, None] * 1.5 + jit * (brk ** 1.5 * 0.9)[:, None]
    pos[:, 2] += 0.1 * np.sin(tau * 5 + pid % 7) * brk
    keep = (brk == 0) | (pid % 9 == 0)
    fade = np.clip(1 - tau / 1.5, 0, 1) * keep
    flying = brk > 0

    depth = pos @ CAM_D
    nd = nrm @ CAM_D
    nrm = np.where((nd < 0)[:, None], -nrm, nrm)
    nd = np.abs(nd)
    diff = np.maximum(nrm @ LIGHT_N, 0)
    spec = np.maximum(nrm @ HALF, 0) ** 24
    lum = (0.25 + 0.95 * diff + 0.3 * (1 - nd) ** 2) * (0.35 + 0.65 * ao)
    dof = 0.55 + 0.45 * np.clip((depth + 0.8) / 1.6, 0, 1)
    rgb = (col * lum[:, None] * 1.2 + 0.45 * spec[:, None]) * (dof * fade)[:, None]
    lumt = np.clip((lum + 0.45 * spec) * dof * fade, 0, 1)

    k = 5.0 / (5.0 - depth)
    sx = pos[:, 0] * k
    sy = (pos @ CAM_UP) * k
    c = ((W / 2 + sx * SCALE) / cw).astype(int)
    r = ((CY - sy * SCALE) / ch).astype(int)
    ok = (c >= 0) & (c < COLS) & (r >= 0) & (r < ROWS) & (fade > 0)
    idx = np.nonzero(ok)[0]
    ids = r[idx] * COLS + c[idx]
    order = np.argsort(-depth[idx], kind="stable")
    uid, first = np.unique(ids[order], return_index=True)
    win = idx[order[first]]

    Lg = np.zeros(ROWS * COLS)
    Cg = np.zeros((ROWS * COLS, 3))
    Dg = np.full(ROWS * COLS, -3.0)
    Fg = np.zeros(ROWS * COLS, bool)
    Pg = np.zeros(ROWS * COLS, int)
    Lg[uid] = lumt[win]
    Cg[uid] = rgb[win]
    Dg[uid] = depth[win]
    Fg[uid] = flying[win]
    Pg[uid] = pid[win]
    sh = (ROWS, COLS)
    return Lg.reshape(sh), Cg.reshape(sh + (3,)), Dg.reshape(sh), Fg.reshape(sh), Pg.reshape(sh)

def edge_chars(filled, D):

    F = np.where(filled, D, -3.0)
    Fp = np.pad(F, 1, constant_values=-3.0)
    nbs = [Fp[1:-1, 2:], Fp[1:-1, :-2], Fp[2:, 1:-1], Fp[:-2, 1:-1]]
    jump = np.max([np.abs(F - nb) for nb in nbs], axis=0)
    edge = filled & (jump > 0.35) & ((jump > 2.0) | (F >= np.max(nbs, axis=0)))
    gx = (Fp[1:-1, 2:] - Fp[1:-1, :-2]) / cw
    gy = (Fp[2:, 1:-1] - Fp[:-2, 1:-1]) / ch
    a = np.degrees(np.arctan2(-gx, -gy)) % 180
    ech = np.select([(a < 22.5) | (a >= 157.5), a < 67.5, a < 112.5], ["-", "/", "|"], "\\")
    return edge, ech

def text_layer(t, gen):

    row = int(CY / ch)
    out = []
    blink = (t * 2.2) % 1 < 0.55
    if t < 0.3:
        if blink:
            out.append(("_", COLS // 2, row, (220, 230, 255)))
    elif t < 0.85:
        n = int((t - 0.3) / 0.55 * len(TYPED)) + 1
        s = TYPED[:n] + "_"
        c0 = COLS // 2 - len(TYPED) // 2
        for i, chr_ in enumerate(s):
            out.append((chr_, c0 + i, row, (220, 230, 255)))
    elif t < 1.3:
        f = 1 - (t - 0.85) / 0.45
        c0 = COLS // 2 - len(TYPED) // 2
        for i, chr_ in enumerate(TYPED):
            if gen.random() < 0.6:
                chr_ = GLITCH[gen.integers(len(GLITCH))]
            if gen.random() < f:
                v = int(255 * f)
                out.append((chr_, c0 + i, row, (v, int(v * 0.6), int(v * 0.75))))
    elif t > 9.35 and blink:
        out.append(("_", COLS // 2, row, (220, 230, 255)))
    return out

glyph_cache = {}

def glyph(chr_, rgb):
    key = (chr_, rgb)
    s = glyph_cache.get(key)
    if s is None:
        s = font.render(chr_, True, rgb)
        glyph_cache[key] = s
    return s

def main():
    gen = np.random.default_rng(5)
    first_on = np.full((ROWS, COLS), np.inf)
    start = time.perf_counter()
    frame = 0
    last_t = 0.0
    clock = pygame.time.Clock()

    while True:
        for e in pygame.event.get():
            if e.type == pygame.QUIT or (e.type == pygame.KEYDOWN and e.key == pygame.K_ESCAPE):
                pygame.quit()
                return
            if e.type == pygame.KEYDOWN and e.key == pygame.K_SPACE:
                start = time.perf_counter()

        if ARGS.at is not None:
            t = ARGS.at
        else:
            t = frame / FPS_RECORD if RECORD else (time.perf_counter() - start) % DURATION
        if t < last_t:
            first_on[:] = np.inf
        last_t = t

        screen.fill(BG)
        blits = []
        res = rasterize(t)
        if res is not None:
            Lg, Cg, Dg, Fg, Pg = res
            filled = Lg > 0.03
            edge, ech = edge_chars(filled, Dg)
            edge &= ~Fg

            chars = RAMP_ARR[np.clip((Lg * (len(RAMP) - 1)).astype(int), 0, len(RAMP) - 1)]
            chars = np.where(Lg > 0.86, MSG_GRID, chars)
            chars = np.where(edge, ech, chars)
            chars = np.where(Fg, LETTERS[Pg % len(LETTERS)], chars)

            colr = np.where(edge[..., None], Cg * 1.25 + 0.06, Cg)

            if t < 5.5:
                first_on[filled & np.isinf(first_on)] = t
                first_on[~filled] = np.inf
                glitching = filled & ((t - first_on) < SETTLE)
                colr = np.where(glitching[..., None], colr * 0.5 + 0.45, colr)
            else:
                glitching = np.zeros_like(filled)

            colr = (np.clip(colr, 0, 1) * 255 // 10 * 10).astype(int)
            rows_i, cols_i = np.nonzero(filled)
            for rr, cc in zip(rows_i.tolist(), cols_i.tolist()):
                chr_ = GLITCH[gen.integers(len(GLITCH))] if glitching[rr, cc] else chars[rr, cc]
                if chr_ == " ":
                    continue
                rgb = (int(colr[rr, cc, 0]), int(colr[rr, cc, 1]), int(colr[rr, cc, 2]))
                if rgb == (0, 0, 0):
                    continue
                blits.append((glyph(chr_, rgb), (cc * cw, rr * ch)))

        for chr_, cc, rr, rgb in text_layer(t, gen):
            blits.append((glyph(chr_, rgb), (cc * cw, rr * ch)))
        screen.blits(blits, doreturn=False)

        for div in (5, 18):
            # Blur at the original 1x size so the glow radius stays the same at every scale.
            small = pygame.transform.smoothscale(screen, (960 // div, 760 // div))
            if ARGS.glow_gain != 1.0:
                px = pygame.surfarray.pixels3d(small)
                px[:] = np.clip(px * ARGS.glow_gain, 0, 255).astype(np.uint8)
                del px
            screen.blit(pygame.transform.smoothscale(small, (W, H)), (0, 0),
                        special_flags=pygame.BLEND_ADD)

        pygame.display.flip()

        if ARGS.at is not None:
            os.makedirs(OUT_DIR, exist_ok=True)
            pygame.image.save(screen, os.path.join(OUT_DIR, "still.png"))
            pygame.quit()
            return

        if RECORD:
            pygame.image.save(screen, f"{OUT_DIR}/f_{frame:04d}.png")
            if frame + 1 >= int(DURATION * FPS_RECORD) or (ARGS.max_frames and frame + 1 >= ARGS.max_frames):
                print("@edjonesv")
                pygame.quit()
                return
        else:
            clock.tick(60)
        frame += 1

if __name__ == "__main__":
    main()

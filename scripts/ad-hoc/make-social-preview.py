#!/usr/bin/env python3
"""Social preview 1280x640 for codex-provider-manager."""

from PIL import Image, ImageDraw, ImageFont

W, H = 1280, 640
BG = (22, 22, 31)        # #16161F
CARD = (38, 38, 53)      # #262635
BORDER = (58, 58, 82)    # #3A3A52
ACCENT = (124, 108, 240) # #7C6CF0
WHITE = (237, 237, 245)  # #EDEDF5
GRAY = (156, 156, 180)   # #9C9CB4

img = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(img)


def font(size, bold=False):
    for cand in (["segoeuib.ttf"] if bold else ["segoeui.ttf"]) + \
                ["/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf" % ("-Bold" if bold else ""),
                 "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf"]:
        try:
            return ImageFont.truetype(cand, size)
        except OSError:
            continue
    return ImageFont.load_default()


f_title = font(64, True)
f_sub = font(30)
f_card = font(26, True)
f_small = font(22)

# header accent bar
d.rectangle([0, 0, W, 8], fill=ACCENT)

d.text((80, 90), "Codex Provider Manager", font=f_title, fill=WHITE)
d.text((80, 180), "Switch, add and manage custom model providers for Codex",
       font=f_sub, fill=GRAY)
d.text((80, 228), "Windows  ·  macOS  ·  Linux", font=f_sub, fill=ACCENT)

# mock provider cards
cx, cy, cw, ch = 80, 296, 540, 92
for i, (name, model, active) in enumerate([
        ("freellm", "auto", True),
        ("lmstudio-local", "qwen/qwen3.5-9b", False),
        ("openai", "built-in account", False)]):
    y = cy + i * (ch + 18)
    d.rounded_rectangle([cx, y, cx + cw, y + ch], radius=14,
                        fill=CARD, outline=ACCENT if active else BORDER,
                        width=3 if active else 1)
    d.text((cx + 24, y + 14), name, font=f_card, fill=WHITE)
    d.text((cx + 24, y + 52), model, font=f_small, fill=GRAY)
    if active:
        d.rounded_rectangle([cx + cw - 130, y + 16, cx + cw - 24, y + 44],
                            radius=10, fill=ACCENT)
        d.text((cx + cw - 118, y + 21), "ACTIVE", font=f_small, fill=WHITE)

# right side: what it does
rx = 700
for i, line in enumerate([
        "->  One-click provider switch",
        "->  API keys never in config files",
        "->  No stale env vars",
        "->  Restore OpenAI in one click",
        "->  GUI + CLI, single binary"]):
    d.text((rx, 320 + i * 56), line, font=f_sub, fill=WHITE)

# footer
d.text((80, H - 46), "github.com/su8z3r0/codex-provider-manager",
       font=f_small, fill=GRAY)

img.save("docs/social-preview.png")
print("saved docs/social-preview.png", img.size)
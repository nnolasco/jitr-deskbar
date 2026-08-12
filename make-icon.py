# Generates app.ico for jitr-deskbar: JITR sky-blue rounded square with a white
# "bar" across the top -- a miniature of the deskbar itself. Rerun after design
# changes; build.ps1 embeds the result via /win32icon.
from PIL import Image, ImageDraw

SKY = (14, 165, 233, 255)      # brand-500 #0ea5e9
DARK = (11, 18, 32, 255)       # bar panel #0b1220
WHITE = (255, 255, 255, 255)

def frame(size):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = max(2, size // 5)
    d.rounded_rectangle([0, 0, size - 1, size - 1], radius=r, fill=SKY)
    # the "bar": dark strip across the upper area with a white title tick
    m = max(1, size // 8)
    bar_h = max(2, size * 5 // 16)
    d.rounded_rectangle([m, m, size - 1 - m, m + bar_h], radius=max(1, r // 2), fill=DARK)
    tick_w = (size - 2 * m) * 5 // 12
    tick_m = max(1, size // 16)
    d.rounded_rectangle(
        [m + tick_m, m + tick_m, m + tick_m + tick_w, m + bar_h - tick_m],
        radius=max(1, size // 32), fill=WHITE)
    return img

sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [frame(s) for s in sizes]
frames[-1].save("app.ico", sizes=[(s, s) for s in sizes],
                append_images=frames[:-1])
print("wrote app.ico")

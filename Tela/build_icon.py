"""Build the app's original geometric icon in Windows sizes, from its vector design."""
from pathlib import Path
from PIL import Image, ImageDraw

directory = Path(__file__).resolve().parent / 'assets'
scale = 4
image = Image.new('RGBA', (256 * scale, 256 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
def rect(bounds, radius, color):
    draw.rounded_rectangle(tuple(int(v * scale) for v in bounds), radius=int(radius * scale), fill=color)
rect((6, 6, 250, 250), 52, '#216FC2')
rect((23, 99, 47, 169), 6, '#A6D3FF')
rect((55, 83, 100, 169), 7, '#D5EAFF')
rect((108, 62, 233, 169), 10, '#FFFFFF')
rect((117, 71, 224, 160), 3, '#216FC2')
def curve(points):
    sampled = []
    for step in range(41):
        t = step / 40
        x = sum(k * p[0] for k, p in zip(((1-t)**3, 3*(1-t)**2*t, 3*(1-t)*t*t, t**3), points))
        y = sum(k * p[1] for k, p in zip(((1-t)**3, 3*(1-t)**2*t, 3*(1-t)*t*t, t**3), points))
        sampled.append((round(x * scale), round(y * scale)))
    draw.line(sampled, fill='#FFFFFF', width=6*scale, joint='curve')
curve(((146,110),(149,88),(180,82),(193,101)))
curve(((195,121),(192,143),(161,149),(148,130)))
for points in (((182,99),(197,109),(198,94)),((160,132),(144,121),(143,136))):
    draw.polygon([(x*scale,y*scale) for x,y in points], fill='#FFFFFF')
draw.line((36 * scale, 193 * scale, 219 * scale, 193 * scale), fill='#FFFFFF', width=10 * scale)
for x in (36, 219):
    draw.ellipse(((x - 5) * scale, 188 * scale, (x + 5) * scale, 198 * scale), fill='#FFFFFF')
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(directory / 'app-icon.png')
image.save(directory / 'app.ico', format='ICO', sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
with Image.open(directory / 'app.ico') as icon:
    sizes = sorted(icon.info['sizes'])
    assert len(sizes) == 7
print('Created PNG, editable SVG and ICO sizes:', sizes)

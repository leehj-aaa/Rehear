from pathlib import Path
import base64, io, re
from PIL import Image

root = Path(__file__).parent
for index in (4, 8):
    path = root / f'slide-{index}.svg'
    svg = path.read_text(encoding='utf-8')
    def optimize(match):
        raw = base64.b64decode(match.group(2))
        im = Image.open(io.BytesIO(raw))
        out = io.BytesIO()
        im.convert('RGB').save(out, format='JPEG', quality=98, subsampling=0)
        new = out.getvalue()
        print(index, im.size, len(raw), len(new))
        if len(new) < len(raw):
            return 'data:image/jpeg;base64,' + base64.b64encode(new).decode()
        return match.group(0)
    svg = re.sub(r'data:image/([^;]+);base64,([A-Za-z0-9+/=]+)', optimize, svg)
    (root / f'slide-{index}-optimized.svg').write_text(svg, encoding='utf-8')
    print('SVG', index, len(svg.encode()))

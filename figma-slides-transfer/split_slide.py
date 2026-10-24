from pathlib import Path
import xml.etree.ElementTree as E
import copy, re

rootpath=Path(__file__).parent
E.register_namespace('', 'http://www.w3.org/2000/svg')
E.register_namespace('xlink', 'http://www.w3.org/1999/xlink')
r=E.parse(rootpath/'slide-4-optimized.svg').getroot()
original=list(r[0])
for i,indices in enumerate(([j] for j in range(8))):
    new=copy.deepcopy(r)
    for node in list(new[0]):new[0].remove(node)
    for index in indices:new[0].append(copy.deepcopy(original[index]))
    defs=new[1]
    defsmap={n.attrib.get('id'):n for n in defs}
    needed=set()
    def collect(n):
        value=E.tostring(n,encoding='unicode')
        refs=set(re.findall(r'url\(#([^\)]+)\)',value))
        refs.update(re.findall(r'href="#([^"]+)"',value))
        for ref in refs-needed:
            needed.add(ref)
            if ref in defsmap:collect(defsmap[ref])
    collect(new[0])
    for n in list(defs):
        if n.attrib.get('id') not in needed:defs.remove(n)
    path=rootpath/f'slide-4-part-{i}.svg'
    E.ElementTree(new).write(path,encoding='utf-8',xml_declaration=True)
    print(path.name,path.stat().st_size)

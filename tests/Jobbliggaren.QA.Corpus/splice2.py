base = open('baseline/layout-corpus-report.baseline.md', encoding='utf-8', newline='').read().split('\r\n')
art  = open('artifacts/layout-corpus-report.md',        encoding='utf-8', newline='').read().split('\r\n')

close = base.index('-->')
appx  = next(i for i, l in enumerate(base) if l.startswith('## Appendix'))
assert base[0] == '<!--' and art[0].startswith('# Jobbliggaren')

# Trim the artifact's trailing blanks and re-add exactly ONE, so the seam matches the
# blank-line convention everywhere else in the file. The first splice left two.
while art and art[-1] == '':
    art.pop()

out = base[:close + 1] + [''] + art + [''] + base[appx:]
open('baseline/layout-corpus-report.baseline.md', 'w', encoding='utf-8', newline='').write('\r\n'.join(out))
print(f'spliced: header 0..{close}, appendix from {appx}')

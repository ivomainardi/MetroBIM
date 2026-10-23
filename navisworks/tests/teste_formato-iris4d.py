# Formato .iris4d.json: o IrisExport.cs grava todos os campos do exemplo exemplo/cronograma.iris4d.json.
# O mesmo exemplo fica no Iris (samples/cronograma-navisworks.iris4d.json), que confere do lado dele que o leitor
# parseIris4D só lê campos do exemplo. Campo novo no formato: mudar o exemplo nos dois repositórios.
import json, re, sys, pathlib
R = pathlib.Path(__file__).resolve().parent.parent
cs = (R / 'IrisExport' / 'IrisExport.cs').read_text(encoding='utf-8')
J = json.loads((R / 'exemplo' / 'cronograma.iris4d.json').read_text(encoding='utf-8'))
noExemplo = set(J) | {k for t in J['tarefas'] for k in t}
gravados = set(re.findall(r'(?:Campo|Nome)\("(\w+)"', cs))
falta = sorted(noExemplo - gravados)
ok = not falta and J.get('formato') == 'iris-4d' and '"iris-4d"' in cs and 'IsDataSourceRootTask' in cs and 'GetSelectedItems' in cs
print('no exemplo:', sorted(noExemplo)); print('gravados pelo plugin:', sorted(gravados)); print('faltam no plugin:', falta)
print('OK' if ok else 'FALHOU'); sys.exit(0 if ok else 1)

# Gera src/MetroSP/Ids/CorrecoesIfc.Dados.cs a partir dos esquemas oficiais (pip install ifcopenshell).
#   python tools/gerar-correcoes-ifc.py src/MetroSP/Ids/CorrecoesIfc.Dados.cs
import ifcopenshell.ifcopenshell_wrapper as w, sys

def dados(sn):
    s = w.schema_by_name(sn)
    ents = {e.name(): e for e in s.entities()}
    def sub(e, b):
        while e is not None:
            if e.name() == b: return True
            e = e.supertype()
        return False
    simples, elementos, distrib = [], [], []
    for n, e in sorted(ents.items()):
        if e.is_abstract(): continue
        at = [a.name() for a in e.all_attributes()]
        if sub(e, 'IfcElement'):
            elementos.append(n.upper())
            if sub(e, 'IfcDistributionElement'): distrib.append(n.upper())
            t = ents.get(n + 'Type')
            if len(at) == 9 and at[8] == 'PredefinedType' and t is not None and not t.is_abstract():
                ta = [a.name() for a in t.all_attributes()]
                if len(ta) == 10 and ta[9] == 'PredefinedType': simples.append(n)
    enum = None
    for a in ents['IfcDistributionSystem'].all_attributes():
        if a.name() == 'PredefinedType':
            t = a.type_of_attribute()
            while hasattr(t, 'declared_type'): t = t.declared_type()
            enum = list(t.enumeration_items())
    return simples, elementos, distrib, enum

def cs(arr, ind='            '):
    out, line = [], ind
    for a in arr:
        t = '"%s", ' % a
        if len(line) + len(t) > 130: out.append(line.rstrip()); line = ind
        line += t
    out.append(line.rstrip().rstrip(','))
    return '\n'.join(out)

s4, e4, d4, n4 = dados('IFC4')
s43, e43, d43, n43 = dados('IFC4X3_ADD2')
blocos = [('Simples4', s4), ('Simples4x3', s43), ('Elementos', sorted(set(e4) | set(e43))),
          ('Distribuicao', sorted(set(d4) | set(d43))), ('Sistemas4', n4), ('Sistemas4x3', n43)]
txt = ['// Gerado a partir dos esquemas oficiais IFC4 (ADD2 TC1) e IFC4X3_ADD2 (IfcOpenShell) por tools/gerar-correcoes-ifc.py.',
       '// Não editar à mão.',
       '// Simples: classes de elemento com 8 atributos + PredefinedType, e o tipo IfcXxxType com 9 + PredefinedType',
       '// (a troca de classe só muda o nome e o PredefinedType). Elementos e Distribuicao: subclasses concretas de IfcElement e',
       '// IfcDistributionElement (os dois esquemas, em maiúsculas). Sistemas: valores do IfcDistributionSystemEnum.',
       'namespace MetroSP.Ifc', '{', '    public static partial class CorrecoesIfc', '    {']
for nome, arr in blocos:
    txt.append('        private static readonly string[] %s =' % nome)
    txt.append('        {')
    txt.append(cs(arr))
    txt.append('        };')
txt += ['    }', '}', '']
open(sys.argv[1], 'w', encoding='utf-8').write('\n'.join(txt))

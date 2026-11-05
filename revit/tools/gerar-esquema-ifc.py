# Gera src/MetroSP/Ids/EsquemaIfc.Dados.cs a partir dos esquemas oficiais (pip install ifcopenshell).
#   python tools/gerar-esquema-ifc.py src/MetroSP/Ids/EsquemaIfc.Dados.cs
import ifcopenshell.ifcopenshell_wrapper as w, sys
def schema(name):
    s = w.schema_by_name(name)
    ents = {e.name(): e for e in s.entities()}
    def is_sub(e, base):
        while e is not None:
            if e.name() == base: return True
            e = e.supertype()
        return False
    def ptypes(e):
        for a in e.all_attributes():
            if a.name() == 'PredefinedType':
                t = a.type_of_attribute()
                while hasattr(t, 'declared_type'):
                    t = t.declared_type()
                if hasattr(t, 'enumeration_items'):
                    return [v for v in t.enumeration_items() if v != 'NOTDEFINED']
        return []
    prod = {}
    allents = set()
    for n, e in ents.items():
        if is_sub(e, 'IfcObjectDefinition') or n in ('IfcObjectDefinition',):
            allents.add(n)
        if not is_sub(e, 'IfcProduct') or e.is_abstract(): continue
        if n.startswith('IfcStructural') or 'Port' in n or n.endswith('StandardCase') or n in ('IfcVirtualElement','IfcOpeningStandardCase'): continue
        if is_sub(e, 'IfcPort'): continue
        prod[n] = ptypes(e)
    return prod, sorted(allents)
def cs(arr, ind='            '):
    out=[];line=ind
    for a in arr:
        t='"%s", '%a
        if len(line)+len(t)>130: out.append(line.rstrip()); line=ind
        line+=t
    out.append(line.rstrip().rstrip(','))
    return '\n'.join(out)
p4,e4 = schema('IFC4'); p43,e43 = schema('IFC4X3_ADD2')
def prodblock(p):
    items=[]
    for n in sorted(p): items.append(n + ('|' + ','.join(p[n]) if p[n] else ''))
    return cs(items)
print(len(p4),len(p43),len(e4),len(e43), file=sys.stderr)
open(sys.argv[1] if len(sys.argv) > 1 else 'EsquemaIfc.Dados.cs', 'w', encoding='utf-8').write(f'''// Gerado a partir dos esquemas oficiais IFC4 (ADD2 TC1) e IFC4X3_ADD2 (IfcOpenShell). Não editar à mão.
// Produtos: classes concretas derivadas de IfcProduct (sem estruturais, portas, *StandardCase e IfcVirtualElement),
// no formato "Classe|TIPO1,TIPO2" com os valores do PredefinedType (sem NOTDEFINED).
// Entidades: todas as derivadas de IfcObjectDefinition (inclui abstratas e *Type), para os property sets.
namespace MetroSP.Ids
{{
    public static partial class EsquemaIfc
    {{
        private static readonly string[] Produtos4 =
        {{
{prodblock(p4)}
        }};

        private static readonly string[] Produtos4x3 =
        {{
{prodblock(p43)}
        }};

        private static readonly string[] Entidades4 =
        {{
{cs(e4)}
        }};

        private static readonly string[] Entidades4x3 =
        {{
{cs(e43)}
        }};
    }}
}}
''')

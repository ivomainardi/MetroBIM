# Gera os padrões de Pset por disciplina (Civil 3D / IFCINFRAEXPORT).
import csv, json, os, sys, io
# Uso: python gerar.py <pasta de saída> [<MetroSP.bundle/Contents>]   (precisa do ifcopenshell: pip install ifcopenshell)
AQUI = os.path.dirname(os.path.abspath(__file__))
BASE_CSV = os.path.join(AQUI, "..", "..", "MetroSP.bundle", "Contents", "Padrao", "IFCInfraExportPropertyMapping.csv")

def ler_iso():
    """Modelos oficiais de Pset do IFC 4.3 (buildingSMART), que vêm com o ifcopenshell."""
    import ifcopenshell, ifcopenshell.util
    f = ifcopenshell.open(os.path.join(os.path.dirname(ifcopenshell.util.__file__), "schema", "Pset_IFC4X3.ifc"))
    r = {}
    for t in f.by_type("IfcPropertySetTemplate"):
        r[t.Name] = {"Applicable": t.ApplicableEntity, "Type": t.TemplateType,
                     "Props": [{"Name": p.Name, "Measure": getattr(p, "PrimaryMeasureType", None)} for p in t.HasPropertyTemplates or []]}
    return r
ISO = ler_iso()
OUT = sys.argv[1]

M = {"Length":"IfcLengthMeasure","Area":"IfcAreaMeasure","Volume":"IfcVolumeMeasure","Label":"IfcLabel","Text":"IfcText",
     "Identifier":"IfcIdentifier","Boolean":"IfcBoolean","Ratio":"IfcRatioMeasure","PlaneAngle":"IfcPlaneAngleMeasure",
     "LinearVelocity":"IfcLinearVelocityMeasure","LinearStiffness":"IfcLinearStiffnessMeasure","Pressure":"IfcPressureMeasure",
     "MassDensity":"IfcMassDensityMeasure","Date":"IfcDate","Real":"IfcReal","Integer":"IfcInteger","Count":"IfcCountMeasure"}
def tipo_planilha(measure):
    if measure in ("IfcBoolean","IfcLogical"): return "Sim/Não"
    if measure in ("IfcInteger","IfcCountMeasure"): return "Inteiro"
    if measure in ("IfcLabel","IfcText","IfcIdentifier","IfcDate"): return "Texto"
    return "Real"

def md(pset, props):  # props: list of (nome, tipoMetroDATA)
    return ("MetroDATA", pset, [(n, M[t]) for n, t in props])
def iso(pset, names):
    d = ISO[pset]; byname = {p["Name"]: p for p in d["Props"]}
    for n in names: assert n in byname, (pset, n)
    return ("ISO 16739 / IFC 4.3", pset, [(n, byname[n]["Measure"] or "IfcLabel") for n in names])

CODIGO = md("MetroClassificacao", [("CodigoMetro","Identifier")])
CLASSIF = ("Plugin (vira IfcClassificationReference)", "Classification", [("ClassificationCode","IfcIdentifier")])

D = [
 {"id":"via-permanente","nome":"Via permanente","fonte":"MetroDATA|civil › Via Permanente X",
  "entidades":"IfcRail,IfcTrackElement,IfcCourse,IfcElementAssembly,IfcMechanicalFastener,IfcFastener,IfcSlab,IfcBuiltElement",
  "aplica":"Corredor; Sólido 3D; Bloco; Alinhamento; Ponto COGO",
  "psets":[CLASSIF, CODIGO,
    md("Metro",[("Tipo","Label"),("Material","Label"),("Numero","Text"),("Lado","Label"),("Bitola","Length"),("Espacamento","Length"),
       ("Resistencia","Pressure"),("Densidade","MassDensity"),("RigidezEstatica","LinearStiffness"),("RigidezDinamica","LinearStiffness"),
       ("CoeficienteAbsorcaoSonora","Text"),("LimitadorCarga","Length"),("PIV","Label"),("Angulo","PlaneAngle"),("Azimute","PlaneAngle"),
       ("RaioHorizontal","Length"),("RaioVertical","Length"),("Espiral","Length"),("Superelevacao","Length"),("Distancia","Length"),
       ("VelocidadeMaximaHorizontal","LinearVelocity"),("VelocidadeMaximaVertical","LinearVelocity"),("VelocidadeVertical","LinearVelocity"),
       ("CoordenadaE","Length"),("CoordenadaN","Length")]),
    md("MetroLoc",[("Quilometragem","Length"),("CotaTB","Length")]),
    md("MetroQto",[("Comprimento","Length"),("Largura","Length"),("Altura","Length"),("Espessura","Length"),("Area","Area"),("Volume","Volume")]),
    iso("Pset_RailTypeRail",["PositionInTrack","RailElementaryLength","IsStainless"]),
    iso("Pset_TrackElementTypeSleeper",["SleeperType","FasteningType","IsElectricallyInsulated"]),
    iso("Pset_CourseCommon",["NominalThickness","NominalWidth","NominalLength"]),
    iso("Pset_MechanicalFastenerTypeRailJoint",["IsJointInsulated","IsCWRJoint","RailGap"]),
    iso("Pset_ElementAssemblyTypeTurnoutPanel",["TypeOfTurnout","TurnoutCurvedRadius","BranchLineDirection","TrackGaugeLength"]),
  ],
  "remaps":[("PropertySet","Corridor Property Data – User Defined","Volume","MetroQto","Volume"),
            ("PropertySet","Corridor Property Data – User Defined","Lenght","MetroQto","Comprimento"),
            ("PropertySet","Corridor Property Data – User Defined","StartStation","MetroLoc","Quilometragem"),
            ("General","Properties","Easting","Metro","CoordenadaE"),
            ("General","Properties","Northing","Metro","CoordenadaN")]},
 {"id":"remanejamento-interferencias","nome":"Remanejamento de interferências","fonte":"MetroDATA|civil › Cadastros e Remanejamento Y",
  "entidades":"IfcPipeSegment,IfcPipeFitting,IfcDistributionChamberElement,IfcMember,IfcCableCarrierSegment,IfcCableSegment,IfcBuiltElement",
  "aplica":"Tubo (gravidade); Estrutura (gravidade); Tubo de pressão; Conexão de pressão; Acessório de pressão; Bloco; Sólido 3D",
  "psets":[CLASSIF, CODIGO,
    md("Metro",[("Nome","Label"),("Descricao","Text"),("CodigoElemento","Identifier"),("Tipo","Label"),("TipoRede","Label"),("TipoSecao","Label"),
       ("Material","Label"),("DiametroNominal","Length"),("Declividade","Ratio"),("Estado","Label"),("Responsavel","Label"),
       ("DataInspecao","Date"),("TipoInspecao","Label"),("ResponsavelInspecao","Label"),("PossuiLuminaria","Boolean"),("PossuiTransformador","Boolean")]),
    md("MetroLoc",[("CoordenadaEInicio","Length"),("CoordenadaNInicio","Length"),("CoordenadaEFim","Length"),("CoordenadaNFim","Length"),
       ("CotaTopoInicio","Length"),("CotaTopoFim","Length"),("CotaFundoInicio","Length"),("CotaFundoFim","Length"),
       ("GeratrizSuperior","Length"),("GeratrizInferior","Length"),
       ("CoordenadaEEstrutura","Length"),("CoordenadaNEstrutura","Length"),("CotaTopoEstrutura","Length"),("CotaFundoEstrutura","Length")]),
    md("MetroQto",[("Comprimento","Length"),("Espessura","Length")]),
    iso("Pset_PipeSegmentTypeCommon",["NominalDiameter","InnerDiameter","OuterDiameter","WorkingPressure"]),
    iso("Pset_DistributionChamberElementTypeManhole",["InvertLevel","SoffitLevel","WallMaterial","AccessCoverMaterial","HasSteps"]),
    iso("Pset_MemberTypePost",["NominalHeight"]),
  ],
  "remaps":[]},
 {"id":"viario-urbano","nome":"Viário urbano","fonte":"MetroDATA|civil › Viário",
  "entidades":"IfcCourse,IfcPavement,IfcKerb,IfcSlab,IfcBuiltElement",
  "aplica":"Corredor; Sólido 3D; Linha característica",
  "psets":[CLASSIF, CODIGO,
    md("Metro",[("Nome","Label"),("Descricao","Text"),("Tipo","Label"),("Material","Label"),("Estado","Label"),
       ("DeclividadeLongitudinal","Ratio"),("DeclividadeTransversal","Ratio"),("AreaSecao","Area")]),
    md("MetroLoc",[("InicioEstaca","Text"),("FimEstaca","Text")]),
    md("MetroQto",[("Comprimento","Length"),("Espessura","Length"),("Area","Area"),("Volume","Volume")]),
    iso("Pset_CourseCommon",["NominalThickness","NominalWidth","NominalLength"]),
    iso("Pset_PavementCommon",["NominalThickness","NominalWidth","StructuralSlope"]),
    iso("Pset_KerbCommon",["CombinedKerbGutter","Upstand","Mountable"]),
    iso("Pset_PrecastKerbStone",["NominalHeight","NominalWidth","NominalLength","TypeDesignation"]),
  ],
  "remaps":[("PropertySet","Corridor Property Data – User Defined","Volume","MetroQto","Volume"),
            ("PropertySet","Corridor Property Data – User Defined","Lenght","MetroQto","Comprimento"),
            ("PropertySet","Corridor Property Data – User Defined","StartStation","MetroLoc","InicioEstaca"),
            ("PropertySet","Corridor Property Data – User Defined","EndStation","MetroLoc","FimEstaca")]},
 {"id":"drenagem","nome":"Drenagem","fonte":"MetroDATA|civil › Hidráulica (tubos, poços de visita, caixas e canaletas)",
  "entidades":"IfcPipeSegment,IfcPipeFitting,IfcDistributionChamberElement,IfcWasteTerminal,IfcBuiltElement",
  "aplica":"Tubo (gravidade); Estrutura (gravidade); Corredor; Sólido 3D",
  "psets":[CLASSIF, CODIGO,
    md("Metro",[("Sistema","Label"),("TipoEscoamento","Label"),("MaterialPrincipal","Label"),("DiametroNominal","Length"),("Inclinacao","Ratio")]),
    md("MetroQto",[("Comprimento","Length"),("Largura","Length"),("Altura","Length")]),
    iso("Pset_PipeSegmentTypeCommon",["NominalDiameter","InnerDiameter","OuterDiameter"]),
    iso("Pset_PipeSegmentOccurrence",["Gradient","InvertElevation"]),
    iso("Pset_PipeSegmentTypeCulvert",["InternalWidth","ClearDepth"]),
    iso("Pset_PipeSegmentTypeGutter",["Slope","IsCovered"]),
    iso("Pset_DistributionChamberElementTypeManhole",["InvertLevel","SoffitLevel","WallMaterial","AccessCoverMaterial","HasSteps"]),
    iso("Pset_WasteTerminalTypeGullySump",["GullyType","NominalSumpLength","NominalSumpWidth","NominalSumpDepth","OutletConnectionSize"]),
  ],
  "remaps":[("PropertySet","Corridor Property Data – User Defined","Lenght","MetroQto","Comprimento")]},
 {"id":"terraplenagem","nome":"Terraplenagem","fonte":"MetroDATA|civil › Geotecnia Y (escavações, reaterros e espalhamentos)",
  "entidades":"IfcEarthworksCut,IfcEarthworksFill,IfcEarthworksElement,IfcGeotechnicalStratum,IfcBuiltElement",
  "aplica":"Superfície TIN; Corredor; Sólido 3D; Linha característica",
  "psets":[CLASSIF, CODIGO,
    md("Metro",[("Nome","Label"),("Material","Label"),("Etapa","Label")]),
    md("MetroLoc",[("Nivel","Label")]),
    md("MetroQto",[("Volume","Volume")]),
    iso("Pset_TrenchExcavationCommon",["NominalDepth","NominalWidth"]),
    iso("Pset_TransitionSectionCommon",["NominalLength"]),
  ],
  "remaps":[("PropertySet","Corridor Property Data – User Defined","Volume","MetroQto","Volume")]},
]

# Psets que o IFCINFRAEXPORT cria e não são ISO nem MetroDATA: lidos do mapeamento atual (+ grupos conhecidos da Autodesk).
base = list(csv.reader(open(BASE_CSV, encoding="utf-8-sig")))
hdr, rows = base[0], base[1:]
iso_names = set(ISO)
def padrao(n): return n in iso_names or n == "Classification"
naopadrao = [r for r in rows if not padrao(r[0])]
# Psets ISO do mapeamento atual que vieram de um desenho (Pset_SlabCommon...): ficam desligados (não são da disciplina).
iso_atuais = [r for r in rows if padrao(r[0]) and r[0] != "Classification"]

def ents_iso(ps):
    r = []
    for e in ISO[ps]["Applicable"].split(","):
        e = e.split("/")[0].strip()
        if e.endswith("Type") or e in r: continue
        r.append(e)
    return ",".join(r)

os.makedirs(OUT, exist_ok=True)
resumo = []
for d in D:
    pasta = os.path.join(OUT, d["id"]); os.makedirs(pasta, exist_ok=True)
    linhas = [hdr]
    # 1) desligados
    remapeados = {(g, c) for _, g, c, _, _ in d["remaps"]}
    for r in naopadrao + iso_atuais:
        if (r[4], r[5]) in remapeados: continue
        linhas.append([r[0], r[1], "FALSE", r[3], r[4], r[5]])
    # 2) remapeados: propriedade nativa do Civil 3D gravada no Pset padrão
    for src, grp, civ, ps, pr in d["remaps"]:
        linhas.append([ps, pr, "TRUE", src, grp, civ])
    # 3) Psets padrão (property sets do desenho com o mesmo nome)
    templates = []
    for origem, ps, props in d["psets"]:
        ent = d["entidades"] if ps not in ("Classification",) else "IfcElement"
        if origem.startswith("ISO"): ent = ISO[ps]["Applicable"].split(",")[0].split("/")[0] if False else ent
        tp = {"Name": ps, "Description": f"{origem} · {d['nome']}", "TemplateType": "NOTDEFINED",
              "ApplicableEntities": ents_iso(ps) if origem.startswith("ISO") else ent, "PropertyTemplates": []}
        for n, mt in props:
            linhas.append([ps, n, "TRUE", "PropertySet", ps, n])
            tp["PropertyTemplates"].append({"Name": n, "Description": "", "PrimaryMeasureType": mt})
        # remaps para este pset entram no modelo também
        templates.append(tp)
    for src, grp, civ, ps, pr in d["remaps"]:
        tp = next(t for t in templates if t["Name"] == ps)
        assert any(p["Name"] == pr for p in tp["PropertyTemplates"]), (ps, pr)
    # tirar linhas repetidas (mesmo Pset/propriedade vindo de PropertySet direto e de remapeamento é intencional: fontes diferentes)
    with open(os.path.join(pasta, "IFCInfraExportPropertyMapping.csv"), "w", encoding="utf-8-sig", newline="") as f:
        csv.writer(f, lineterminator="\r\n").writerows(linhas)
    with open(os.path.join(pasta, "IFCInfraExportPropertyMapping.json"), "w", encoding="utf-8") as f:
        json.dump({"PropertySetTemplates": templates}, f, indent=2, ensure_ascii=False)
    # planilha do Montar property sets (cria as Property Set Definitions no DWG). IFC = Não: o mapeamento do perfil já cuida.
    with open(os.path.join(pasta, "planilha-property-sets.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f, delimiter=";", lineterminator="\r\n")
        w.writerow(["Property set","Propriedade","Tipo","Valor padrão","Aplica-se a","Descrição","IFC"])
        for origem, ps, props in d["psets"]:
            for n, mt in props:
                w.writerow([ps, n, tipo_planilha(mt), "", d["aplica"], f"{origem} · {d['nome']}", "Não"])
    resumo.append((d, sum(len(p[2]) for p in d["psets"]), len(d["remaps"])))

# lista dos não padrão
with open(os.path.join(OUT, "psets-nao-padrao-autodesk.csv"), "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f, delimiter=";", lineterminator="\r\n"); w.writerow(["Property set","Propriedade","Origem no Civil 3D","Por que sai"])
    for r in naopadrao:
        why = {"DrawingProperties":"Propriedades do arquivo DWG","General":"Propriedade interna do objeto (Civil 3D/.NET)","UserDefined":"Campo criado pelo usuário no DWG"}.get(r[3], "Property set do desenho fora do padrão")
        w.writerow([r[0], r[1], f"{r[3]} › {r[4]}", why])
    for r in iso_atuais:
        w.writerow([r[0], r[1], f"{r[3]} › {r[4]}", "ISO, mas veio de um desenho de edificação (não é das disciplinas)"])
from collections import Counter
c = Counter(r[0] for r in naopadrao)
json.dump({"naopadrao": c.most_common(), "iso_atuais": sorted(set(r[0] for r in iso_atuais)),
           "disc": [(d["nome"], n, r, [(p[1], p[0], len(p[2])) for p in d["psets"]]) for d, n, r in resumo]},
          open(os.path.join(OUT, "_resumo.json"), "w"), ensure_ascii=False, indent=1)
print(json.dumps(c.most_common(), ensure_ascii=False)); print([(d["nome"], n, r) for d, n, r in resumo])

# ---- perfis pré-configurados no bundle (argv[2] = pasta Contents do MetroSP.bundle)
if len(sys.argv) > 2:
    import shutil
    contents = sys.argv[2]; padrao = os.path.join(contents, "Padrao")
    for d in D:
        dest = os.path.join(contents, "Perfis", d["nome"])
        if os.path.isdir(dest): shutil.rmtree(dest)
        os.makedirs(dest)
        for f in ("IfcInfraConfiguration.json", "IfcInfraExportMapping.json"):
            shutil.copy(os.path.join(padrao, f), dest)
        cfg = json.load(open(os.path.join(padrao, "MetroSP.ifc.json"), encoding="utf-8"))
        if d["id"] == "viario-urbano":
            cfg["Estrutura"]["Instalacoes"][0]["Tipo"] = "IfcRoad"
        with open(os.path.join(dest, "MetroSP.ifc.json"), "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2, ensure_ascii=False); f.write("\n")
        src = os.path.join(OUT, d["id"])
        shutil.copy(os.path.join(src, "IFCInfraExportPropertyMapping.csv"), dest)
        shutil.copy(os.path.join(src, "IFCInfraExportPropertyMapping.json"), dest)
        shutil.copy(os.path.join(src, "planilha-property-sets.csv"), os.path.join(dest, "MetroSP.property-sets.csv"))

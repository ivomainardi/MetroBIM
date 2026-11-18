# Perfis por disciplina (property sets)

`gerar.py` monta os perfis de `MetroSP.bundle/Contents/Perfis/<disciplina>` a partir do perfil MetroSP (`Padrao`),
do MetroDATA|civil (Airtable, listas dentro do script) e dos modelos oficiais de Pset do IFC 4.3 que vêm com o ifcopenshell.

    pip install ifcopenshell
    python gerar.py <pasta de saída> ../../MetroSP.bundle/Contents

Na pasta de saída ficam, por disciplina, o mapeamento (.csv e .json) e a planilha, e `psets-nao-padrao-autodesk.csv`.
Para mudar uma disciplina, edite a lista `D` no script e rode de novo.

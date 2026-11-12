# MetroBIM · Plugins MetroSP

Plugins padrão do **Metrô de São Paulo** para os softwares Autodesk, feitos pela Ação BIM · Metrô.
Todos seguem a mesma identidade visual (preto, laranja `#FF9900` e cinzas) e a mesma aba **MetroSP** na Ribbon,
com interface em português pensada para projetistas.

| Plugin | Software | O que faz | Pasta |
|---|---|---|---|
| **MetroSP para Revit** | Revit 2022 a 2027 | Parâmetros compartilhados, classificação, padrões Metrô, conferência de entrega e exportação IFC no padrão do Metrô | [revit/](revit/) |
| **MetroSP para Civil 3D** | Civil 3D 2025 a 2027 | Exportação IFC 4.3 com o alinhamento 3D (planta e perfil), dados do projeto, do terreno e das edificações | [civil3d/](civil3d/) |
| **MetroSP para Navisworks** | Navisworks Manage e Simulate 2025 a 2027 | Exporta o cronograma do TimeLiner para o Iris, já ligado aos objetos pelo GlobalId do IFC | [navisworks/](navisworks/) |

## Instalar

Baixe o `MetroSP-Setup-<versão>.exe` da última versão em [Releases](../../releases/latest) e rode.
O instalador é um só para o Revit e o Civil 3D (o Navisworks entra depois): ele mostra só os programas encontrados no computador
e instala só para o seu usuário, sem precisar de administrador. As novidades ficam em [NOTAS-DA-VERSAO.md](NOTAS-DA-VERSAO.md).

## Pastas

- `revit/`, `civil3d/`, `navisworks/`: o código de cada plugin, com o histórico dos repositórios antigos.
- `instalador/`: o instalador único (Inno Setup) e as imagens dele.
- `build.ps1`: compila os plugins e monta o instalador.

## Publicar uma versão

1. Escreva a nova seção `## x.y.z` no topo do `NOTAS-DA-VERSAO.md` (é ela que define a versão do instalador).
2. Navisworks (por enquanto fora do instalador): para incluir, rode `.\build.ps1 -SoNavisworks` num PC com o
   Navisworks e anexe o `navisworks-<versão>.zip` a uma release `vx.y.z` em rascunho.
3. Em **Actions › Release › Run workflow**. Ele compila, monta o `MetroSP-Setup-x.y.z.exe` e publica a release.

Para compilar no PC: `.\build.ps1` (precisa do SDK .NET 8 e 10 e do Inno Setup 6).
O GitHub Actions só roda à mão: nada roda a cada push.

## Navisworks e Iris

O plugin do Navisworks grava o arquivo `.iris4d.json` que o Iris lê. O exemplo `navisworks/exemplo/cronograma.iris4d.json`
é o contrato: o mesmo exemplo fica no repositório do Iris, com testes dos dois lados. Campo novo: gravar no plugin,
pôr no exemplo dos dois lados e só então ler no Iris.

## Marcas

Os nomes e logos do Metrô de São Paulo e da Ação BIM pertencem aos seus donos e não fazem parte de nenhuma licença
deste repositório.

# Plugin do Navisworks: Exportar para o Iris

Botão no Navisworks Manage ou Simulate (2025, 2026 e 2027) que grava o cronograma do TimeLiner num arquivo `.iris4d.json`. O Iris lê esse arquivo em **Fiscalização › Cronograma › Importar › Navisworks**.

O arquivo leva, para cada tarefa, os objetos ligados a ela pelo **GlobalId do IFC**. Isso vale para os conjuntos e também para as seleções feitas à mão ("Explicit Selection"), que não saem no CSV do TimeLiner. No Iris, com o mesmo IFC aberto, as tarefas já entram vinculadas aos objetos.

O plugin só lê o documento: não muda nada no Navisworks.

## Instalar

1. Instale o .NET SDK 8 ou mais novo, uma vez só: `winget install Microsoft.DotNet.SDK.8`.
2. Feche o Navisworks.
3. No PowerShell, dentro desta pasta, rode `.\instalar.ps1`.
   - O script compila o plugin para cada Navisworks 2025, 2026 e 2027 que achar instalado (Manage ou Simulate).
   - Depois copia a DLL e os ícones do botão (`iris_16.png` e `iris_32.png`) para `C:\Program Files\Autodesk\Navisworks <Manage|Simulate> <versão>\Plugins\IrisExport\`, a pasta que o Navisworks lê ao abrir. Por isso o script pede permissão de administrador.
   - Para uma versão só: `.\instalar.ps1 -Versoes 2026`.
   - Se o PowerShell bloquear o script: `powershell -ExecutionPolicy Bypass -File .\instalar.ps1`.

Se a aba Tool Add-ins 1 não aparecer, confira se `IrisExport.dll` está em `<pasta do Navisworks>\Plugins\IrisExport\` e abra o Navisworks de novo. Se o botão aparecer sem o ícone do Iris, faltam os dois PNG nessa mesma pasta.

O plugin precisa ser compilado em cada versão porque as DLLs da API (`Autodesk.Navisworks.Api.dll` e `Autodesk.Navisworks.Timeliner.dll`) vêm do Navisworks instalado e não vão para o repositório. Para um Navisworks em outra pasta, use:

```
dotnet build IrisExport\IrisExport.csproj -c Release -p:NavisVersion=2026 -p:NavisDir="D:\Autodesk\Navisworks Manage 2026"
```

## Usar

1. No Navisworks, carregue o **mesmo IFC** que vai ser aberto no Iris. O GlobalId é o elo entre os dois.
2. Deixe o TimeLiner pronto, com as tarefas ligadas aos objetos.
3. Na aba **Tool Add-ins 1 › Exportar para o Iris**, escolha onde salvar o `.iris4d.json`.
4. No Iris, abra o IFC. Depois, em Cronograma › Importar › Navisworks, escolha o `.iris4d.json`.
5. Na próxima versão do cronograma, exporte de novo e use **Atualizar** no Iris. As tarefas casam pelo ID de sincronização (ou pelo GUID da tarefa no Navisworks).

Durante a exportação, o plugin grava um `.iris4d.log` ao lado do arquivo, com uma linha por tarefa e o tempo de cada uma. Se o Navisworks fechar no meio, a última linha do `.log` mostra em qual tarefa parou. Uma tarefa em que o Navisworks dá erro ao ler a seleção sai sem objetos, com o campo `erro`, e a exportação continua.

Se aparecer o aviso de objetos sem GlobalId, o modelo carregado no Navisworks não é o IFC, ou o leitor de IFC não gravou o GlobalId. O campo `diagnostico` do arquivo mostra as propriedades do primeiro objeto sem GlobalId, para descobrir onde ele está.

## Formato `.iris4d.json` (versão 1)

```json
{
  "formato": "iris-4d", "versao": 1, "origem": "Navisworks", "programa": "Navisworks 23.0…",
  "documento": "C:\\…\\obra.nwf", "exportadoEm": "2026-10-22T14:00:00", "dataStatus": null,
  "modelos": ["obra-EST.ifc", "obra-ARQ.ifc"],
  "tarefas": [
    { "id": "27176", "codigo": "1", "nome": "PLANEJAMENTO", "nivel": 0, "resumo": true, "tipo": "",
      "inicio": "2022-02-01", "fim": "2023-05-13", "inicioReal": null, "fimReal": null, "avanco": 0,
      "campos": {}, "globalIds": [] },
    { "id": "4031", "codigo": "3", "nome": "Estacas", "nivel": 1, "resumo": false, "tipo": "Construct",
      "inicio": "2024-01-11", "fim": "2024-02-20", "inicioReal": "2024-01-15", "fimReal": null, "avanco": 40,
      "campos": { "User 1": "BEP_EST-STC" }, "selecao": "explicita", "globalIds": ["2O2Fr$t4X7Zf8NOew3FLOH"] }
  ],
  "semGlobalId": 0
}
```

- Tarefas em ordem, da de cima para as de dentro; `nivel` 0 é o primeiro nível. A raiz da fonte de dados ("New Data Source (Root)") não entra.
- `id` é o ID de sincronização do TimeLiner (ou o GUID da tarefa) e mantém os vínculos nas atualizações.
- Datas no formato AAAA-MM-DD. `avanco` vai de 0 a 100.
- `campos` traz User 1…10 preenchidos. `selecao` é `explicita`, `conjuntos` ou `busca`.
- `globalIds` são os objetos da tarefa. Se a tarefa aponta para um grupo (arquivo, pavimento, camada), entram os objetos de dentro dele. Se aponta só para a geometria, entra o objeto acima.

O formato é aberto: outro programa (ou um script) que grave esse JSON também entra no Iris pelo mesmo caminho.

## Ligação com o Iris

O código do plugin fica só neste repositório. O Iris (`ivomainardi/Iris`) lê o `.iris4d.json` em `web/src/js/85-cron.js` (`parseIris4D`). O contrato entre os dois é o exemplo `exemplo/cronograma.iris4d.json`, igual ao `samples/cronograma-navisworks.iris4d.json` do Iris:

- aqui, `python tests/teste_formato-iris4d.py` confere que o `IrisExport.cs` grava todos os campos do exemplo (roda antes de cada versão, fora do GitHub; no GitHub só à mão, em Actions › Testes › Run workflow);
- no Iris, `tests/teste_formato-iris4d.py` confere que o leitor só usa campos do exemplo e, com este repositório clonado ao lado (`../MetroSP_Navisworks`), que o plugin grava cada um.

Campo novo no formato: gravar no plugin, pôr no exemplo aqui e no Iris, e só então ler no Iris.

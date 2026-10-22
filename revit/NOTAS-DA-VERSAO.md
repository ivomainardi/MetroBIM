# MetroSP 0.2.0

Revit 2025, 2026 e 2027.

## Novo: Exportar IFC (painel IFC / IDS)

Exporta o IFC com o setup do exportador (.json) e o mapeamento de property sets (.txt) guardados
fora do modelo, para toda a equipe usar a mesma configuração.

- **Origem dos arquivos:** projeto do Autodesk Forma, lido pelo Desktop Connector, ou uma pasta no computador ou na rede.
- **Pasta padrão no Forma**, igual em todo projeto: `Project Files\BIM\Configuracoes IFC\` com `Setups\`,
  `PropertySets\` e `Mapeamentos\`. Escolhendo `Civil.json`, o `Civil.txt` de cada subpasta entra sozinho.
  O botão **Criar pasta padrão** monta a estrutura num projeto novo. O caminho pode ser alterado na janela.
- **Projeto na nuvem:** se o modelo aberto está no Forma, o projeto dele já vem selecionado.
- **Conferência antes de exportar:** lê o .txt e avisa quais parâmetros do mapeamento não existem no modelo.
- **Vista:** exporta o modelo inteiro ou só o que está visível numa vista 3D. Pasta de saída e nome do arquivo ficam lembrados.
- **Caminho do .txt:** o caminho gravado no .json, que é o da máquina de quem salvou, é corrigido sozinho procurando o arquivo pelo nome.

Limites: vínculos não são exportados; a classificação e o endereço do projeto usados são os salvos no modelo;
o modelo de mapeamento guardado no .rvt (Revit 2025+) é ignorado, para valer o .txt.

## Também nesta versão

- **Renomear Compartilhado:** renomeia o parâmetro também em todas as famílias carregadas, inclusive as
  aninhadas, mantendo o GUID e os valores.
- **Verificar Classificação:** remove a fórmula da família quando ela impede gravar a classificação.
- **Ribbon:** painel com as logos.
- **Janelas de lista:** filtros por coluna, seleção por categoria e "aplicar só as selecionadas".

## Instalar

Feche o Revit, extraia o .zip e rode `Instalar.bat`. Ele instala em todas as versões do Revit
encontradas no computador. Para desinstalar: `Instalar.bat -Desinstalar`.

using System;
using MetroSP.Escrita;

// Casos de teste das regras do Corrigir escrita. Sai com código 1 se algum falhar.
var p = new PadraoEscrita
{
    Termos = { "PTL", "Pr_20_30", "ESTACAO-X" },
    Correcoes = { "plataf = plataforma", "metrosp = MetroSP" },
};
var c = new CorretorEscrita(p);
int falhas = 0, total = 0;

void T(string valor, string esperado)
{
    total++;
    ResultadoEscrita r = c.Analisar(valor);
    if (r.Sugestao == esperado) return;
    falhas++;
    Console.WriteLine($"FALHA [{valor}] -> [{r.Sugestao}], esperado [{esperado}]  ({r.Problemas})");
}

// Espaços
T("Concreto  armado", "Concreto armado");
T("  Concreto ", "Concreto");
T("Concreto\u00A0armado", "Concreto armado");
T("Concreto\u200Barmado", "Concretoarmado");
T("Linha1\nLinha2", "Linha1 Linha2");
T(@"C:\Projetos\estacao  nova.rvt", @"C:\Projetos\estacao nova.rvt");
T("https://metro.sp.gov.br/estacao", "https://metro.sp.gov.br/estacao");

// Pontuação
T("Pr_20_30_10 : Concreto", "Pr_20_30_10: Concreto");
T("aço ,concreto", "aço, concreto");
T("aço,concreto", "aço, concreto");
T("IfcWall,IfcSlab", "IfcWall,IfcSlab");
T("2,5", "2,5");
T("( armado )", "(armado)");
T("Fim .", "Fim.");
T("v1.2", "v1.2");
T("10:30", "10:30");

// Número e unidade
T("Laje 10m", "Laje 10 m");
T("Fck 25MPa", "Fck 25 MPa");
T("e=1,5cm", "e=1,5 cm");
T("Área 20m2", "Área 20 m2");
T("1 º pavimento", "1º pavimento");
T("MB-20.00.00.00-4U1-001-0", "MB-20.00.00.00-4U1-001-0");
T("E1-10m", "E1-10m");

// Acentuação
T("ESTACAO DA LUZ", "ESTAÇÃO DA LUZ");
T("Estacao", "Estação");
T("estaçao", "estação");
T("Nivel 01", "Nível 01");
T("Instalacao eletrica", "Instalação elétrica");
T("Padroes", "Padrões");
T("Corrimao", "Corrimão");
T("Pressao", "Pressão");
T("Emergencia", "Emergência");
T("Acessivel", "Acessível");
T("Sanitario masculino", "Sanitário masculino");
T("hidraulico", "hidráulico");
T("Sistema mecanico", "Sistema mecânico");
T("Geologico", "Geológico");
T("órgao", "órgão");
T("pre-moldado", "pré-moldado");
T("idéia", "ideia");
T("Ítem 3", "Item 3");
T("freqüência", "frequência");
T("Nao se aplica", "Não se aplica");
T("Sao Paulo", "São Paulo");
T("Does not apply", "Does not apply");
T("Section Area", "Section Area");
T("Scenario A", "Scenario A");
T("Travel", "Travel");
T("IfcBuildingStorey", "IfcBuildingStorey");
T("caos", "caos");
T("Calculo", "Calculo");
T("AÇÃO", "AÇÃO");
T("Ação", "Ação");

// Termos e correções do padrão
T("PTL", "PTL");
T("Bloco ESTACAO-X", "Bloco ESTACAO-X");
T("plataf norte", "plataforma norte");
T("Plataf norte", "Plataforma norte");
T("metrosp", "MetroSP");

// Codificação e caracteres
T("EstaÃ§Ã£o", "Estação");
T("Pe\u0301", "Pé");
T("d´água", "d'água");
T("“Teste”", "\"Teste\"");

total++;
if (c.Analisar("Esta??o").Revisar == null) { falhas++; Console.WriteLine("FALHA letra perdida não foi apontada"); }

Console.WriteLine(falhas == 0 ? $"{total} casos, todos certos." : $"{falhas} de {total} casos falharam.");
return falhas == 0 ? 0 : 1;

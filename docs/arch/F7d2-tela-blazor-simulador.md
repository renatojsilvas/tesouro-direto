# F7d-2 — Tela Blazor do Simulador

## Componentes

### Simulador.razor (`/simulador`)

Pagina interativa com form + resultados. `@rendermode InteractiveServer`.

**Form:**
- Select: titulo (carregado de GET /titulos via HttpClient)
- Input: valor investido (decimal)
- Input: data compra (date)
- Input: taxa contratada (decimal %)
- Input: projecao anual (decimal %, opcional)
- Button: Simular

**Resultado:**
- Card com valores: investido, bruto, rendimento bruto
- Tabela de tributos aplicados (nome, aliquota, valor)
- Card com totais: tributos, liquido, rendimento liquido
- Tabela de cupons (se aplicavel)

**Estados:**
- Loading titulo (dropdown)
- Loading simulacao (spinner)
- Erro (mensagem)
- Resultado (cards + tabelas)

## Infra necessaria (primeira tela Blazor → API)

### HttpClient em Program.cs

```csharp
builder.Services.AddHttpClient("TesouroDiretoApi", client =>
{
    client.BaseAddress = new Uri(configuration["ApiSettings:BaseUrl"]);
    client.DefaultRequestHeaders.Add("X-Api-Key", configuration["ApiSettings:ApiKey"]);
});
```

### appsettings.json

```json
"ApiSettings": {
  "BaseUrl": "http://localhost:5000",
  "ApiKey": "CHANGE-ME-IN-PRODUCTION"
}
```

### NavMenu.razor

Adicionar link "Simulador" (`href="simulador"`).

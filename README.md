# fluent-api-migrator
 Migrador de EDMX para Fluent API.
 Criado a partir da do projeto [edmx-to-fluentApi-migrator](https://github.com/AntonDambrouski/edmx-to-fluentApi-migrator).

# Instruções para uso

Tornar Start project o **fluent-api-migrator.Console**;

No **app.config** alterar os appsettings para definir:
- **edmxFilePath**: é o arquivo EDMX que deve ser feito a leitura;
- **outputDirectory**: é para onde deve ser enviado o arquivo traduzido.
https://github.com/Lucas-r-o/fluent-api-migrator/blob/d26de18ec6a3467d754b7d26efcbfec7c53b3c15/src/fluent-api-migrator.Console/App.config#L10-L13

# Exemplo de uso

Verificar o [LegacyAppDemo](https://github.com/Lucas-r-o/fluent-api-migrator/tree/main/src/LegacyAppDemo.Console)

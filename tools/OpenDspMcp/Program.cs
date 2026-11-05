using System.Text;

// JSON-RPC stdio only. No diagnostics on stdout and no remote service/API keys.
Console.InputEncoding=new UTF8Encoding(false);
Console.OutputEncoding=new UTF8Encoding(false);
if(args.Length==2&&args[0]=="--checks")return await McpChecks.Run(args[1]);

var protocol=new McpProtocol(PipeTransport.Request);
while(await Console.In.ReadLineAsync() is { } line){
 if(string.IsNullOrWhiteSpace(line))continue;
 string? response=await protocol.Handle(line);
 if(response!=null)await Console.Out.WriteLineAsync(response);
}
return 0;

$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\NativeApo.cs'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('{4780004E-7133-41D8-8C74-660DADD2C0EE}","{DBBF8-4D7E-4F13-A101-1A8DF4E12C0C}","{B9C6E4DD-3C18-4F1D-A1C1-C0C0C0C0C0C0}"}','{4780004E-7133-41D8-8C74-660DADD2C0EE}"}')
$a=$a.Replace('foreach(var value in saved){var key=value.Store=="fx"?fx:props;string? current=key.GetValue(value.Name) as string;if(value.Name==EffectKey&&!string.Equals(current,ClassId,StringComparison.OrdinalIgnoreCase))continue;value.Restore(key);}','foreach(var value in saved){var key=value.Store=="fx"?fx:props;string? current=key.GetValue(value.Name) as string;if(value.Name==EffectKey&&!string.Equals(current,ClassId,StringComparison.OrdinalIgnoreCase))continue;value.Restore(key);}fx.SetValue(ModesKey,new[]{"{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}","{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}","{4780004E-7133-41D8-8C74-660DADD2C0EE}"},RegistryValueKind.MultiString);')
[IO.File]::WriteAllText($p,$a)
'poison removed'

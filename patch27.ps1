$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\DsdOutputChecks.cs'
$a=[IO.File]::ReadAllText($p)
$anchor='void Check(bool yes,string name){if(!yes)throw new Exception(name);cases.Add(name);}'
$golden=$anchor+'
  // DoP 1.1 黄金样本：按规范手算逐字节锁死线格式（独立于被测代码，防系统性位序/布局错误）
  var g=new byte[24];DopFrame.Pack(0x8000,0x0001,0x05,g,0,3);
  Check(g[0]==0x00&&g[1]==0x80&&g[2]==0x05&&g[3]==0x01&&g[4]==0x00&&g[5]==0x05,"golden_24bit_t0_is_msb_of_word");
  DopFrame.Pack(0x1234,0xABCD,0xFA,g,6,3);
  Check(g[6]==0x34&&g[7]==0x12&&g[8]==0xFA&&g[9]==0xCD&&g[10]==0xAB&&g[11]==0xFA,"golden_24bit_little_endian_word_and_marker");
  DopFrame.Pack(0x8000,0x0001,0x05,g,12,4);
  Check(g[12]==0x00&&g[13]==0x00&&g[14]==0x80&&g[15]==0x05&&g[16]==0x00&&g[17]==0x01&&g[18]==0x00&&g[19]==0x05,"golden_32bit_left_aligned_low8_padding");
  DopFrame.Pack(0xFFFF,0x0000,0xFA,g,0,3);
  Check(g[0]==0xFF&&g[1]==0xFF&&g[2]==0xFA&&g[3]==0x00&&g[4]==0x00&&g[5]==0xFA,"golden_word_edges");'
$a=$a.Replace($anchor,$golden)
[IO.File]::WriteAllText($p,$a)
'golden added'

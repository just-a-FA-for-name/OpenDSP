namespace OpenEQ;

public static class RackDefaults {
 public static Preset Reset(Preset current,string section){
  var next=current.Copy();var original=next.Output;
  switch(section){
   case "output":next.Output=new OutputConfig{Compressor=original.Compressor,Matrix=original.Matrix,DcBlock=original.DcBlock,Spatial=original.Spatial};break;
   case "compressor":next.Output.Compressor=new CompConfig();break;
   case "matrix":next.Output.Matrix=new MatrixConfig();break;
   case "spatial":next.Output.Spatial=new SpatialConfig();break;
   case "all":next.Output=new OutputConfig();break;
   default:throw new ArgumentException("section must be output, compressor, matrix, spatial or all");
  }
  next.Validate();return next;
 }
}

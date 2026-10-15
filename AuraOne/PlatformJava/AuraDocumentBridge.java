package za.co.auraone.assistant.bridge;
import android.content.Context;
import android.graphics.*;
import android.graphics.pdf.PdfRenderer;
import android.os.ParcelFileDescriptor;
import android.util.Base64;
import java.io.*;
import org.json.JSONObject;
/** Renders only the PDF the owner selected into the app's private cache. */
public final class AuraDocumentBridge {
 public static String render(Context context,String path,int number){
  try{
   File file=new File(path);String prefix=context.getCacheDir().getCanonicalPath()+File.separator;
   if(!file.getCanonicalPath().startsWith(prefix)||!file.isFile())throw new IOException("Unselected PDF");
   try(ParcelFileDescriptor descriptor=ParcelFileDescriptor.open(file,ParcelFileDescriptor.MODE_READ_ONLY);PdfRenderer renderer=new PdfRenderer(descriptor)){
    if(number<1||number>renderer.getPageCount())throw new IOException("Page out of range");
    try(PdfRenderer.Page page=renderer.openPage(number-1)){
     double scale=Math.min(1.8,1280.0/Math.max(page.getWidth(),page.getHeight()));
     Bitmap bitmap=Bitmap.createBitmap(Math.max(1,(int)(page.getWidth()*scale)),Math.max(1,(int)(page.getHeight()*scale)),Bitmap.Config.ARGB_8888);
     try{new Canvas(bitmap).drawColor(Color.WHITE);page.render(bitmap,null,null,PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);ByteArrayOutputStream bytes=new ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.JPEG,75,bytes);return new JSONObject().put("pages",renderer.getPageCount()).put("image",Base64.encodeToString(bytes.toByteArray(),Base64.NO_WRAP)).toString();}finally{bitmap.recycle();}
    }
   }
  }catch(Exception error){return "ERROR:PDF page could not be rendered ("+error.getClass().getSimpleName()+").";}
 }
}

const {chromium}=require('playwright');(async()=>{const b=await chromium.launch();const p=await b.newPage();
for(const k of process.argv.slice(3)) for(const s of [32,16]){await p.setViewportSize({width:s,height:s});await p.goto('file://'+__dirname+'/render.html?k='+k+'&s='+s);await p.screenshot({path:process.argv[2]+'/'+k+s+'.png',omitBackground:true,clip:{x:0,y:0,width:s,height:s}});}
await b.close()})();

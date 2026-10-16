import {createHmac,randomBytes,timingSafeEqual,createHash} from 'node:crypto';
const providers={
  google:{authorize:'https://accounts.google.com/o/oauth2/v2/auth',token:'https://oauth2.googleapis.com/token',scopes:'https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/calendar.readonly https://www.googleapis.com/auth/drive.metadata.readonly'},
  github:{authorize:'https://github.com/login/oauth/authorize',token:'https://github.com/login/oauth/access_token',scopes:'read:user offline_access'}
};
const callback='za.co.auraone.assistant:/oauth/callback';
const b64=value=>Buffer.from(value).toString('base64url');
export function createBroker(options={}){
 const env=options.env??process.env, fetcher=options.fetch??fetch, now=options.now??(()=>Date.now());
 const key=options.secret??randomBytes(32),used=new Map(),rates=new Map();
 const base=(options.baseUrl??env.PUBLIC_BASE_URL??env.RENDER_EXTERNAL_URL??'').replace(/\/$/,'');
 const enabled=name=>!!(env[name.toUpperCase()+'_CLIENT_ID']&&env[name.toUpperCase()+'_CLIENT_SECRET']);
 const sign=value=>createHmac('sha256',key).update(value).digest('base64url');
 function seal(value){const encoded=b64(JSON.stringify(value));return encoded+'.'+sign(encoded);}
 function unseal(value){
  if(typeof value!=='string'||value.length>1800)throw Error('invalid_state');
  const [encoded,signature,...extra]=value.split('.');const expected=sign(encoded??'');
  if(extra.length||!signature||signature.length!==expected.length||!timingSafeEqual(Buffer.from(signature),Buffer.from(expected)))throw Error('invalid_state');
  let state;try{state=JSON.parse(Buffer.from(encoded,'base64url').toString());}catch{throw Error('invalid_state');}
  if(!providers[state.provider]||state.exp<now()||state.exp>now()+610000||!validChallenge(state.challenge)||!validNonce(state.nonce))throw Error('invalid_state');
  return state;
 }
 const validChallenge=value=>typeof value==='string'&&/^[A-Za-z0-9_-]{43}$/.test(value);
 const validNonce=value=>typeof value==='string'&&/^[A-Za-z0-9_-]{32,128}$/.test(value);
 function json(res,status,data){res.writeHead(status,{'Content-Type':'application/json; charset=utf-8','Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','Content-Security-Policy':"default-src 'none'; frame-ancestors 'none'"});res.end(JSON.stringify(data));}
 async function body(req){
  if(!(req.headers['content-type']??'').startsWith('application/json'))throw Error('json_required');
  let size=0,chunks=[];for await(const chunk of req){size+=chunk.length;if(size>16384)throw Error('body_too_large');chunks.push(chunk);}
  try{return JSON.parse(Buffer.concat(chunks).toString());}catch{throw Error('invalid_json');}
 }
 function requireReady(name){if(!enabled(name)||!base.startsWith('https://'))throw Error('registration_required');}
 async function token(name,form){
  const response=await fetcher(providers[name].token,{method:'POST',redirect:'error',headers:{'Content-Type':'application/x-www-form-urlencoded','Accept':'application/json'},body:new URLSearchParams({...form,client_id:env[name.toUpperCase()+'_CLIENT_ID'],client_secret:env[name.toUpperCase()+'_CLIENT_SECRET']}),signal:AbortSignal.timeout(15000)});
  const reader=response.body?.getReader();let chunks=[],size=0;
  if(!reader)throw Error('provider_failed');
  try{while(true){const next=await reader.read();if(next.done)break;size+=next.value.length;if(size>32768)throw Error('provider_response_too_large');chunks.push(next.value);}}finally{await reader.cancel().catch(()=>{});}
  let result;try{result=JSON.parse(Buffer.concat(chunks).toString());}catch{throw Error('provider_failed');}
  if(!response.ok||result.error||typeof result.access_token!=='string'||result.access_token.length>8192)throw Error('authorization_failed');
  const safe={access_token:result.access_token,token_type:'Bearer',scope:typeof result.scope==='string'?result.scope:'',expires_in:typeof result.expires_in==='number'?result.expires_in:(name==='google'?3600:null)};
  if(typeof result.refresh_token==='string'&&result.refresh_token.length<=8192)safe.refresh_token=result.refresh_token;
  return safe;
 }
 return async(req,res)=>{
  try{
   const url=new URL(req.url,'https://broker.invalid');
   if(req.url.length>8192)return json(res,414,{error:'url_too_long'});
   for(const [entry,expiry] of used)if(expiry<now())used.delete(entry);
   for(const [entry,rate] of rates)if(rate.until<now())rates.delete(entry);
   const ip=req.socket.remoteAddress??'unknown',rate=rates.get(ip)??{count:0,until:now()+60000};rate.count++;rates.set(ip,rate);
   if(rate.count>120)return json(res,429,{error:'rate_limited'});
   if(req.method==='GET'&&(url.pathname==='/'||url.pathname==='/health'))return json(res,200,{service:'aura-one-oauth-broker',status:'running',providers:{google:enabled('google'),github:enabled('github')},storesAccountTokens:false});
   if(req.method==='GET'&&url.pathname==='/config')return json(res,200,{baseUrl:base,androidCallback:callback,google:{clientId:env.GOOGLE_CLIENT_ID??null,ready:enabled('google'),callbackUrl:base+'/oauth/google/callback'},github:{clientId:env.GITHUB_CLIENT_ID??null,ready:enabled('github'),callbackUrl:base+'/oauth/github/callback'}});
   const route=/^\/oauth\/(google|github)\/(start|callback|exchange|refresh)$/.exec(url.pathname);
   if(!route)return json(res,404,{error:'not_found'});
   const [,name,action]=route;
   if(action==='callback'&&req.method==='GET'){
    const signed=url.searchParams.get('state'),state=unseal(signed);
    if(state.provider!==name)throw Error('invalid_state');
    const destination=new URL(callback);destination.searchParams.set('state',signed);
    if(url.searchParams.has('error'))destination.searchParams.set('error','authorization_denied');
    else{const code=url.searchParams.get('code');if(!code||code.length>2048)throw Error('invalid_code');destination.searchParams.set('code',code);}
    res.writeHead(302,{'Location':destination.toString(),'Cache-Control':'no-store','Referrer-Policy':'no-referrer'});return res.end();
   }
   if(req.method!=='POST')return json(res,405,{error:'method_not_allowed'});
   requireReady(name);const input=await body(req);
   if(action==='start'){
    if(!validChallenge(input.challenge)||!validNonce(input.nonce))throw Error('invalid_request');
    const state=seal({provider:name,challenge:input.challenge,nonce:input.nonce,exp:now()+600000,id:randomBytes(16).toString('hex')});
    const destination=new URL(providers[name].authorize);
    destination.search=new URLSearchParams({client_id:env[name.toUpperCase()+'_CLIENT_ID'],redirect_uri:base+'/oauth/'+name+'/callback',response_type:'code',scope:providers[name].scopes+(name==='github'&&input.includePrivate===true?' repo':''),state,code_challenge:input.challenge,code_challenge_method:'S256'}).toString();
    if(name==='google'){destination.searchParams.set('access_type','offline');destination.searchParams.set('prompt','consent select_account');destination.searchParams.set('include_granted_scopes','true');}
    return json(res,200,{authorizationUrl:destination.toString(),state,expires_in:600});
   }
   if(action==='exchange'){
    const state=unseal(input.state);
    if(state.provider!==name||typeof input.code!=='string'||!input.code||input.code.length>2048||typeof input.verifier!=='string'||!/^[A-Za-z0-9._~-]{43,128}$/.test(input.verifier))throw Error('invalid_request');
    if(createHash('sha256').update(input.verifier).digest('base64url')!==state.challenge||input.nonce!==state.nonce)throw Error('invalid_proof');
    if(used.has(state.id))throw Error('already_redeemed');
    used.set(state.id,state.exp);
    return json(res,200,await token(name,{grant_type:'authorization_code',code:input.code,code_verifier:input.verifier,redirect_uri:base+'/oauth/'+name+'/callback'}));
   }
   if(action==='refresh'){
    if(typeof input.refresh_token!=='string'||input.refresh_token.length<8||input.refresh_token.length>8192||/\s/.test(input.refresh_token))throw Error('invalid_request');
    return json(res,200,await token(name,{grant_type:'refresh_token',refresh_token:input.refresh_token}));
   }
   return json(res,405,{error:'method_not_allowed'});
  }catch(error){
   const allowed=['invalid_state','json_required','invalid_json','body_too_large','registration_required','authorization_failed','invalid_code','invalid_request','invalid_proof','already_redeemed','provider_response_too_large'];
   const code=allowed.includes(error.message)?error.message:'provider_failed';
   json(res,code==='registration_required'?503:code.startsWith('provider')?502:400,{error:code});
  }
 };
}

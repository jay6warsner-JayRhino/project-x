import test from 'node:test';
import assert from 'node:assert/strict';
import {createServer} from 'node:http';
import {createHash} from 'node:crypto';
import {createBroker} from './broker.mjs';
async function fixture(t,configured=true){
 const calls=[],env=configured?{GOOGLE_CLIENT_ID:'unit-google-public-id',GOOGLE_CLIENT_SECRET:'unit-server-only-secret',GITHUB_CLIENT_ID:'unit-github-public-id',GITHUB_CLIENT_SECRET:'unit-github-server-secret'}:{};
 const server=createServer(createBroker({env,baseUrl:'https://aura-broker.example',secret:Buffer.alloc(32,7),fetch:async(url,request)=>{calls.push({url,request});return new Response(JSON.stringify({access_token:'unit-authorized-access',refresh_token:'unit-authorized-refresh',expires_in:3600}),{status:200});}}));
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));t.after(()=>new Promise(resolve=>server.close(resolve)));
 const url='http://127.0.0.1:'+server.address().port;
 const post=(path,body)=>fetch(url+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 const verifier='v'.repeat(64),nonce='n'.repeat(43),challenge=createHash('sha256').update(verifier).digest('base64url');
 return {url,post,calls,env,verifier,nonce,challenge};
}
test('public readiness omits secrets and unconfigured authorization fails closed',async t=>{
 const f=await fixture(t,false),config=await(await fetch(f.url+'/config')).json();
 assert.equal(config.google.clientId,null);assert.equal(config.google.ready,false);
 assert.equal((await f.post('/oauth/google/start',{nonce:f.nonce,challenge:f.challenge})).status,503);
 assert.equal((await fetch(f.url+'/health')).status,200);
});
test('PKCE and signed state bind one-time exchange to the native request',async t=>{
 const f=await fixture(t),started=await(await f.post('/oauth/google/start',{nonce:f.nonce,challenge:f.challenge})).json();
 const authorization=new URL(started.authorizationUrl);assert.equal(authorization.hostname,'accounts.google.com');assert.equal(authorization.searchParams.get('code_challenge'),f.challenge);
 assert.equal(authorization.searchParams.get('redirect_uri'),'https://aura-broker.example/oauth/google/callback');
 const callback=await fetch(f.url+'/oauth/google/callback?code=unit-code&state='+encodeURIComponent(started.state),{redirect:'manual'});
 const native=new URL(callback.headers.get('location'));assert.equal(native.protocol,'za.co.auraone.assistant:');assert.equal(native.searchParams.get('code'),'unit-code');
 const request={state:started.state,code:'unit-code',nonce:f.nonce,verifier:f.verifier};
 assert.equal((await f.post('/oauth/google/exchange',{...request,verifier:'x'.repeat(64)})).status,400);
 const result=await(await f.post('/oauth/google/exchange',request)).json();assert.equal(result.access_token,'unit-authorized-access');
 assert.equal(f.calls.length,1);assert.equal(f.calls[0].url,'https://oauth2.googleapis.com/token');
 assert.equal(f.calls[0].request.body.get('client_secret'),f.env.GOOGLE_CLIENT_SECRET);
 assert.equal((await f.post('/oauth/google/exchange',request)).status,400);
});
test('forged states and arbitrary callbacks are rejected',async t=>{
 const f=await fixture(t),response=await fetch(f.url+'/oauth/google/callback?state=forged&code=unit',{redirect:'manual'});
 assert.equal(response.status,400);assert.equal(response.headers.get('location'),null);
 assert.equal((await f.post('/oauth/google/start',{nonce:'short',challenge:f.challenge,callback:'https://evil.example'})).status,400);
});
test('refresh uses a fixed provider and keeps server secrets out of public config',async t=>{
 const f=await fixture(t);const config=await(await fetch(f.url+'/config')).text();assert.ok(!config.includes('unit-server-only-secret'));
 const result=await(await f.post('/oauth/github/refresh',{refresh_token:'unit-refresh-value'})).json();assert.ok(result.access_token);
 assert.equal(f.calls[0].url,'https://github.com/login/oauth/access_token');assert.equal(f.calls[0].request.body.get('grant_type'),'refresh_token');
});
test('request and endpoint restrictions reject arbitrary network forwarding',async t=>{
 const f=await fixture(t);assert.equal((await f.post('/oauth/other/refresh',{})).status,404);
 assert.equal((await f.post('/oauth/google/refresh',{refresh_token:'bad whitespace'})).status,400);
 assert.equal((await f.post('/oauth/google/start',{nonce:f.nonce,challenge:f.challenge,padding:'x'.repeat(17000)})).status,400);
 assert.equal(f.calls.length,0);
});

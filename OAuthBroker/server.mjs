import {createServer} from 'node:http';
import {createBroker} from './broker.mjs';
const server=createServer(createBroker());
server.requestTimeout=20000;server.headersTimeout=10000;
server.listen(Number(process.env.PORT??10000),'0.0.0.0',()=>console.log('Aura One authorization broker listening; account tokens are not stored or logged.'));
process.on('SIGTERM',()=>{server.close(()=>process.exit(0));setTimeout(()=>process.exit(1),5000).unref();});

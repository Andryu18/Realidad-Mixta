import makeWASocket from '@whiskeysockets/baileys'
import qrcode from 'qrcode-terminal'

const sock = makeWASocket({})

sock.ev.on('connection.update', ({ connection, qr }) => {
  if (qr) {
    // Render the QR code yourself — printQRInTerminal is deprecated.
    qrcode.generate(qr, { small: true })
  }

  if (connection === 'open') {
    console.log('Connected to WhatsApp')
  }
})

const {
  default: makeWASocket,
  useMultiFileAuthState,
  DisconnectReason,
  fetchLatestBaileysVersion
} = require('@whiskeysockets/baileys');


async function connectToWhatsApp() {
  const { state, saveCreds } = await useMultiFileAuthState('auth_info_baileys');
  const { version } = await fetchLatestBaileysVersion();

  const sock = makeWASocket({
    version,
    auth: state,
    logger: P({ level: 'silent' }),
    markOnlineOnConnect: false,
    syncFullHistory: false,
    browser: ['Android', 'Chrome', '10.0.0']
  });

  sock.ev.on('creds.update', saveCreds);

  sock.ev.on('connection.update', (update) => {
    const { connection, lastDisconnect, qr } = update;

    if (qr) {
      console.log('\n📌 ESCANEA ESTE QR DESDE WHATSAPP:\n');
      qrcode.generate(qr, { small: true });
    }

    if (connection === 'close') {
      const reason = lastDisconnect?.error?.output?.statusCode;
      console.log('❌ Conexión cerrada:', reason);

      if (reason === DisconnectReason.loggedOut) {
        console.log('🛑 Sesión cerrada desde el dispositivo. Borra la carpeta auth_info_baileys');
        process.exit(1);
      }

      console.log('🔁 Reintentando...');
      connectToWhatsApp();
    }

    if (connection === 'open') console.log('🚀 Conectado a WhatsApp!');
  });

  sock.ev.on('messages.upsert', async ({ messages }) => {
  const msg = messages[0];
  if (!msg.message || msg.key.fromMe) return;

  const from = msg.key.remoteJid;
  let text = "";

  const m = msg.message;

  if (m.conversation) text = m.conversation;
  else if (m.extendedTextMessage?.text) text = m.extendedTextMessage.text;
  else if (m.imageMessage?.caption) text = m.imageMessage.caption;
  else if (m.videoMessage?.caption) text = m.videoMessage.caption;
  else if (m.buttonsResponseMessage?.selectedButtonId) text = m.buttonsResponseMessage.selectedButtonId;
  else if (m.listResponseMessage?.singleSelectReply?.selectedRowId) text = m.listResponseMessage.singleSelectReply.selectedRowId;
  else if (m.templateButtonReplyMessage?.selectedId) text = m.templateButtonReplyMessage.selectedId;
  else if (m.interactiveResponseMessage?.body?.text) text = m.interactiveResponseMessage.body.text;

  text = text.trim();

  console.log(`📩 ${from}: ${text}`);

  try {
    const webhookURL = 'https://ai.dixelmedia.com/webhook/1466c8ea-0bc8-4d85-b2b3-fd2ed2bb43e6';

    const resp = await axios.post(webhookURL, { from, text });

    const reply =
      typeof resp.data === 'string'
        ? resp.data.trim()
        : resp.data?.reply || '';

    if (reply.length > 0) {
      await sock.sendMessage(from, { text: reply });
      console.log(`📤 Respuesta enviada: ${reply}`);
    } else {
      console.log('⚠ Webhook no envió texto válido');
    }

  } catch (err) {
    console.error('❌ Error webhook:', err.message);
  }
});

}

connectToWhatsApp();
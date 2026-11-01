-- Previews saved by 0.1.81-0.1.83 could contain leftover HTML entities from hidden marketing
-- preheaders. Clearing them makes the app rebuild a clean preview the next time the message is opened.
UPDATE messages SET snippet = '' WHERE snippet LIKE '%&%;%';

const express = require("express");
const os = require("os");
const path = require("path");

const app = express();
const BASE_PORT = Number(process.env.PORT || 3000);
const MAX_PORT_TRIES = 15;
const HOST = "0.0.0.0";
const STATIC_ROOT = __dirname;

app.use((req, res, next) => {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "GET,OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");

  if (req.method === "OPTIONS") {
    res.sendStatus(204);
    return;
  }

  next();
});

app.use(express.static(STATIC_ROOT));

app.get("/health", (_req, res) => {
  res.json({ status: "ok" });
});

app.get("*", (_req, res) => {
  res.sendFile(path.join(STATIC_ROOT, "index.html"));
});

function startServer(preferredPort, attempt = 0) {
  const port = preferredPort + attempt;

  const server = app
    .listen(port, HOST, () => {
      console.log(`AR CAD Viewer running at http://localhost:${port}`);
      const lanUrls = getLanUrls(port);
      if (lanUrls.length > 0) {
        console.log("Open from mobile (same Wi-Fi):");
        lanUrls.forEach((url) => console.log(`- ${url}`));
      }

      if (attempt > 0) {
        console.log(`Note: port ${preferredPort} was busy, switched to ${port}`);
      }
    })
    .on("error", (error) => {
      if (error.code === "EADDRINUSE" && attempt < MAX_PORT_TRIES) {
        console.warn(`Port ${port} is in use. Retrying on ${port + 1}...`);
        startServer(preferredPort, attempt + 1);
        return;
      }

      console.error("Failed to start AR CAD Viewer server:", error);
      process.exit(1);
    });

  return server;
}

function getLanUrls(port) {
  const interfaces = os.networkInterfaces();
  const urls = [];

  Object.values(interfaces).forEach((values) => {
    (values || []).forEach((entry) => {
      if (!entry || entry.internal || entry.family !== "IPv4") {
        return;
      }

      urls.push(`http://${entry.address}:${port}`);
    });
  });

  return Array.from(new Set(urls));
}

startServer(BASE_PORT);

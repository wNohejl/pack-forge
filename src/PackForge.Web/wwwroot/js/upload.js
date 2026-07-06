// Chunked browser -> blob upload via SAS. Bytes never transit the app server:
// blocks are PUT straight to storage, then committed with a block list.
window.packforge = {
  upload: async function (fileInput, dotnetRef) {
    const file = fileInput.files[0];
    if (!file) throw new Error("No file selected.");

    const beginResp = await fetch("/api/uploads/begin", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fileName: file.name, sizeBytes: file.size }),
    });
    if (!beginResp.ok) throw new Error("begin failed: " + (await beginResp.text()));
    const { id, uploadUrl } = await beginResp.json();

    const chunkSize = 8 * 1024 * 1024;
    const blockIds = [];
    let pos = 0, n = 0;
    while (pos < file.size) {
      const end = Math.min(pos + chunkSize, file.size);
      const blockId = btoa("block-" + String(n).padStart(8, "0"));
      const r = await fetch(`${uploadUrl}&comp=block&blockid=${encodeURIComponent(blockId)}`, {
        method: "PUT",
        body: file.slice(pos, end),
      });
      if (!r.ok) throw new Error(`block ${n} failed: ${r.status}`);
      blockIds.push(blockId);
      pos = end;
      n++;
      await dotnetRef.invokeMethodAsync("OnProgress", Math.round((100 * pos) / file.size));
    }

    const blockListXml =
      '<?xml version="1.0" encoding="utf-8"?><BlockList>' +
      blockIds.map((b) => `<Latest>${b}</Latest>`).join("") +
      "</BlockList>";
    const commit = await fetch(`${uploadUrl}&comp=blocklist`, {
      method: "PUT",
      headers: { "x-ms-blob-content-type": file.type || "application/octet-stream" },
      body: blockListXml,
    });
    if (!commit.ok) throw new Error("commit failed: " + commit.status);

    const complete = await fetch(`/api/uploads/${id}/complete`, { method: "POST" });
    if (!complete.ok) throw new Error("complete failed: " + (await complete.text()));
    return await complete.json();
  },
};

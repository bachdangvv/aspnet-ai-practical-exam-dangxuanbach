"use strict";
const $ = id => document.getElementById(id);
let products = [];
let sessionId = sessionStorage.getItem("smartshop.session") || crypto.randomUUID();
sessionStorage.setItem("smartshop.session", sessionId);
let chatBusy = false;
const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });

async function api(path, body) {
  const response = await fetch(path, body === undefined ? {} : {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
  });
  const data = await response.json();
  if (!response.ok) {
    const validation = data.errors ? Object.values(data.errors).flat().join(" ") : "";
    const retry = response.headers.get("Retry-After");
    throw new Error((data.detail || validation || data.title || "The request failed.") +
      (retry ? ` Try again in ${retry} seconds.` : ""));
  }
  return data;
}

function cell(row, text, className) {
  const td = document.createElement("td");
  if (className) {
    const span = document.createElement("span");
    span.className = className;
    span.textContent = text;
    td.append(span);
  } else td.textContent = text;
  row.append(td);
  return td;
}

async function loadReport() {
  const rows = await api("/api/reports/reviews");
  $("report-body").replaceChildren();
  for (const item of rows) {
    const row = document.createElement("tr");
    cell(row, String(item.no).padStart(2, "0"));
    cell(row, item.productName);
    cell(row, item.totalReviews);
    cell(row, item.positive, "sentiment positive");
    cell(row, item.negative, "sentiment negative");
    cell(row, item.neutral, "sentiment neutral");
    const td = cell(row, "");
    const rating = document.createElement("div");
    rating.className = "rating-cell";
    const star = document.createElement("span"); star.textContent = "★"; star.setAttribute("aria-hidden", "true");
    rating.append(star, document.createTextNode(item.totalReviews ? item.averageRating.toFixed(1) : "—"));
    td.append(rating); $("report-body").append(row);
  }
  const total = rows.reduce((sum, r) => sum + r.totalReviews, 0);
  const positives = rows.reduce((sum, r) => sum + r.positive, 0);
  $("total-reviews").textContent = total;
  $("positive-rate").textContent = total ? `${Math.round(positives / total * 100)}%` : "—";
  $("reviewed-products").textContent = rows.filter(r => r.totalReviews > 0).length;
  $("product-count").textContent = rows.length;
  $("report-note").textContent = `${rows.length} products · Updated ${new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}`;
  if (!rows.length) {
    const tr = document.createElement("tr"); const td = cell(tr, "No products in the catalog yet.");
    td.colSpan = 7; td.className = "table-message"; $("report-body").append(tr);
  }
}

function preview() {
  const product = products.find(p => p.productId === Number($("product-select").value));
  $("generate-description").disabled = !product;
  if (!product) return;
  $("product-name").textContent = product.productName;
  $("product-category").textContent = product.category.toUpperCase();
  $("product-price").textContent = money.format(product.price);
  $("product-features").textContent = product.features;
  $("description-result").hidden = !product.description;
  $("description-result").className = "result";
  $("description-result").textContent = product.description || "";
}

async function loadCatalog() {
  products = await api("/api/products");
  for (const id of ["product-select", "review-product"]) {
    const selected = $(id).value;
    $(id).replaceChildren();
    for (const p of products) {
      const option = document.createElement("option"); option.value = p.productId; option.textContent = p.productName;
      $(id).append(option);
    }
    if (products.some(p => String(p.productId) === selected)) $(id).value = selected;
  }
  $("analyze-review").disabled = !products.length;
  preview();
}

async function refresh() {
  $("refresh").disabled = true; $("page-error").hidden = true;
  try { await Promise.all([loadReport(), loadCatalog()]); }
  catch (error) { $("page-error").textContent = error.message; $("page-error").hidden = false; }
  finally { $("refresh").disabled = false; }
}

function result(id, text, isError = false) {
  $(id).hidden = false; $(id).textContent = text; $(id).className = isError ? "result error" : "result";
}

$("refresh").addEventListener("click", refresh);
$("product-select").addEventListener("change", preview);
$("generate-description").addEventListener("click", async () => {
  const button = $("generate-description"); const selected = $("product-select");
  button.disabled = true; selected.disabled = true; result("description-result", "Writing your product description…");
  try {
    const data = await api(`/api/products/${selected.value}/generatedescription`, {});
    const index = products.findIndex(p => p.productId === data.productId); products[index] = data;
    result("description-result", data.description);
  } catch (error) { result("description-result", error.message, true); }
  finally { button.disabled = false; selected.disabled = false; }
});

$("review-form").addEventListener("submit", async event => {
  event.preventDefault(); const button = $("analyze-review"); button.disabled = true;
  result("review-result", "Analyzing your feedback…");
  try {
    const data = await api("/api/reviews/analyze", {
      productId: Number($("review-product").value), customerName: $("customer-name").value.trim(),
      content: $("review-content").value.trim(), rating: Number($("rating").value)
    });
    result("review-result", `${data.sentiment} · ${Math.round(data.score * 100)}% confidence\n${data.summary}`);
    $("review-content").value = "";
    try { await loadReport(); }
    catch { $("page-error").textContent = "Review saved. Refresh to update the report."; $("page-error").hidden = false; }
  } catch (error) { result("review-result", error.message, true); }
  finally { button.disabled = false; }
});

function chatMessage(text, role) {
  const div = document.createElement("div"); div.className = `chat-message ${role}`; div.textContent = text;
  $("chat-log").append(div); div.scrollIntoView({ block: "nearest", behavior: "smooth" }); return div;
}

$("chat-form").addEventListener("submit", async event => {
  event.preventDefault(); if (chatBusy) return;
  const message = $("chat-message").value.trim(); if (!message) return;
  chatBusy = true; $("send-chat").disabled = true; $("new-chat").disabled = true;
  chatMessage(message, "user"); $("chat-message").value = "";
  const pending = chatMessage("Thinking…", "model");
  try { const data = await api("/api/assistant/chat", { sessionId, message }); pending.textContent = data.reply; }
  catch (error) { pending.textContent = error.message; pending.classList.add("error"); }
  finally { chatBusy = false; $("send-chat").disabled = false; $("new-chat").disabled = false; }
});

for (const button of document.querySelectorAll("[data-prompt]")) button.addEventListener("click", () => {
  if (chatBusy) return; $("chat-message").value = button.dataset.prompt; $("chat-form").requestSubmit();
});
$("new-chat").addEventListener("click", () => {
  if (chatBusy) return; sessionId = crypto.randomUUID(); sessionStorage.setItem("smartshop.session", sessionId);
  $("chat-log").replaceChildren(); chatMessage("A fresh start! Ask me about products in our catalog.", "model");
});
refresh();

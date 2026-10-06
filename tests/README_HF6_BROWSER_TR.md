HF6 tarayıcı smoke testleri gerçek Core'a yazmaz; yalnız test DOM/temsili API fixture kullanır.
Gereksinim: Python 3, pip install playwright, playwright install chromium.
`python tests/test_hf6_customer_browser.py` ve `python tests/test_hf6_trend_8_mock.py`.
Tarayıcının yerel HTTP erişimi engelli CI ortamlarında HTML/CSS/JS inline yüklenir. Bu tarayıcı testleri gerçek Win11/PLC, UAC ve mobil ACK testi yerine geçmez.

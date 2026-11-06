# Contributing

Bug reports, ideas, translations and code are all welcome. For a bigger change, open an issue first so we can talk about it.

## Code

1. Run the tests with `powershell -ExecutionPolicy Bypass -File .\test.ps1`.
2. Build with `powershell -ExecutionPolicy Bypass -File .\build.ps1`.
3. Keep the change small and add a test in `tests/` when you can.

## Translations

Copy `src/diller/en.txt`, name it after the language code, like `de.txt`, and translate the right side of each line. Keep placeholders like `{0}` as they are.

## Please don't

- Commit Spotify tokens, signing keys or any other secret.

## Türkçe

Hata bildirimleri, fikirler, çeviriler ve kod katkıları memnuniyetle karşılanır. Büyük bir değişiklik için önce sorun aç, birlikte konuşalım.

Kod için önce `test.ps1`, sonra `build.ps1` çalıştır. Değişikliği küçük tut, mümkünse `tests/` altına test ekle. Yeni dil için `src/diller/en.txt` dosyasını kopyala ve her satırın sağ tarafını çevir; `{0}` gibi yer tutucuları koru.

Spotify anahtarlarını, imza anahtarlarını veya başka gizli bilgileri ekleme.

// The few parts of Monaco the scenario reaches in the page through window.monaco, without depending on monaco-editor.
interface MonacoModel {
  getValue(): string;
  setValue(value: string): void;
}

interface Window {
  monaco: { editor: { getModels(): MonacoModel[] } };
}

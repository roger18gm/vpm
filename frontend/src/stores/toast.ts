import { defineStore } from "pinia";
import { ref } from "vue";

type ToastKind = "success" | "error";

type ToastMessage = {
  id: number;
  text: string;
  kind: ToastKind;
};

export const useToastStore = defineStore("toast", () => {
  const messages = ref<ToastMessage[]>([]);
  let nextId = 1;

  function push(text: string, kind: ToastKind = "success") {
    const id = nextId++;
    messages.value.push({ id, text, kind });
    window.setTimeout(() => {
      messages.value = messages.value.filter((message) => message.id !== id);
    }, 3500);
  }

  function dismiss(id: number) {
    messages.value = messages.value.filter((message) => message.id !== id);
  }

  return { messages, push, dismiss };
});

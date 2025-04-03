import os, getpass
from dotenv import load_dotenv
import httpx
from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI

# Load environment variables from .env file
load_dotenv()

# def _set_env(var: str):
#     if not os.environ.get(var):
#         os.environ[var] = getpass.getpass(f"{var}: ")

# def _set_env(var: str):
#     if not os.environ.get(var):
#         os.environ[var] = getpass.getpass(f"{var}: ")
#
# _set_env("OPENAI_API_KEY")
# _set_env("LANGCHAIN_API_KEY")
# api_key = os.environ.get("OPENAI_API_KEY")
# print(api_key)

os.environ["LANGCHAIN_TRACING_V2"] = "true"
os.environ["LANGCHAIN_PROJECT"] = "proxy_picker"

proxy_picker_llm = ChatOpenAI(model="gpt-4", temperature=0.1, base_url="https://reverse.onechats.top/v1")

prompt_system = SystemMessage(content="""What's the meaning of life?""")

print(proxy_picker_llm.invoke([prompt_system]))



